using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace FlashColor.Tests;

/// <summary>
/// Fait passer une rangée de pixels dans un pixel shader du tonemap, sur WARP (la carte graphique logicielle de Windows),
/// avec les mêmes entrées que le jeu : t0/s0 l'image, t1/s1 la courbe, b0 et b1 ses constantes, b13 nos réglages (et
/// t2/s2 la courbe du filtre de la GPose choisi, comme le lie le plugin).
/// </summary>
internal sealed unsafe class WarpRenderer : IDisposable
{
    // Triangle qui couvre l'écran ; même sortie que le shader de sommets du jeu (SV_POSITION, puis TEXCOORD0).
    private const string VertexShaderSource = """
        struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Output main(uint id : SV_VertexID)
        {
            Output o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }
        """;

    private readonly ID3D11Device* device;
    private readonly ID3D11DeviceContext* context;
    private readonly ID3D11VertexShader* vertexShader;

    public WarpRenderer()
    {
        ID3D11Device* createdDevice;
        ID3D11DeviceContext* createdContext;
        var level = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0;
        Check(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, HMODULE.NULL, 0, &level, 1, D3D11.D3D11_SDK_VERSION, &createdDevice, null, &createdContext));
        device = createdDevice;
        context = createdContext;

        var code = ShaderCompiler.Compile(VertexShaderSource, "main", "vs_5_0");
        ID3D11VertexShader* vs;
        fixed (byte* p = code)
            Check(device->CreateVertexShader(p, (nuint)code.Length, null, &vs));
        vertexShader = vs;
    }

    /// <summary>Couleurs RGBA en sortie du shader, une par pixel d'entrée.</summary>
    public float[][] Run(byte[] pixelShaderCode, float[][] pixels, ToneMapInputs inputs, ShaderParams parameters, int rows = 1)
    {
        var resources = new List<nint>();
        try
        {
            var image = CreateTexture(pixels, rows, resources);
            var curve = CreateTexture(inputs.Curve, 1, resources);
            var pointSampler = CreateSampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT, resources);
            var linearSampler = CreateSampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR, resources);

            var common = inputs.Common;
            var b0 = CreateConstantBuffer(&common, 16, resources);
            nint b1;
            fixed (Vector4* toneMap = inputs.ToneMapParams)
                b1 = CreateConstantBuffer(toneMap, 32, resources);
            var b13 = CreateConstantBuffer(&parameters, ShaderParams.Size, resources);

            // Comme le plugin : la courbe du filtre choisi, en octets (R8_UNORM), lue en linéaire.
            var filter = parameters.Filter.X > 0f ? GameFilters.Find((uint)parameters.Filter.Y) : null;
            var filterCurve = filter != null ? CreateByteTexture(filter.Curve, DXGI_FORMAT.DXGI_FORMAT_R8_UNORM, resources) : null;

            return Draw(pixelShaderCode, pixels.Length, rows, new[] { image, curve, filterCurve }, new[] { pointSampler, linearSampler, linearSampler },
                [(0, b0), (1, b1), (13, b13)], resources);
        }
        finally
        {
            Release(resources);
        }
    }

    /// <summary>
    /// Le shader de filtre de couleurs du jeu (ColorFilter_DarkBlend.shcd) avec ce filtre de la GPose, réglé comme le jeu
    /// le règle en GPose : la courbe en A8_UNORM (t1), lue au centre de ses cases, force 1, mélange des zones sombres neutre.
    /// </summary>
    public float[][] RunGameFilter(byte[] pixelShaderCode, float[][] pixels, GameFilter filter)
    {
        var resources = new List<nint>();
        try
        {
            var image = CreateTexture(pixels, 1, resources);
            var curve = CreateByteTexture(filter.Curve, DXGI_FORMAT.DXGI_FORMAT_A8_UNORM, resources);
            var pointSampler = CreateSampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT, resources);
            var linearSampler = CreateSampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR, resources);

            nint b0;
            fixed (float* matrix = filter.Matrix)
                b0 = CreateConstantBuffer(matrix, 48, resources);
            float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
            nint b1;
            fixed (float* dark = identity)
                b1 = CreateConstantBuffer(dark, 48, resources);
            var lut = new Vector4(255f / 256f, 0.5f / 256f, 1f, 0f);
            var b2 = CreateConstantBuffer(&lut, 16, resources);
            var noDark = Vector4.Zero;
            var b3 = CreateConstantBuffer(&noDark, 16, resources);

            return Draw(pixelShaderCode, pixels.Length, 1, new[] { image, curve }, new[] { pointSampler, linearSampler },
                [(0, b0), (1, b1), (2, b2), (3, b3)], resources);
        }
        finally
        {
            Release(resources);
        }
    }

    public void Dispose()
    {
        vertexShader->Release();
        context->Release();
        device->Release();
    }

    private float[][] Draw(byte[] pixelShaderCode, int pixelCount, int rows, ID3D11ShaderResourceView*[] views, ID3D11SamplerState*[] samplers,
        (uint Slot, nint Buffer)[] buffers, List<nint> resources)
    {
        var width = (uint)(pixelCount / rows);
        var height = (uint)rows;

        ID3D11PixelShader* ps;
        fixed (byte* p = pixelShaderCode)
            Check(device->CreatePixelShader(p, (nuint)pixelShaderCode.Length, null, &ps));
        resources.Add((nint)ps);

        var targetDescription = TextureDescription(width, height, D3D11_USAGE.D3D11_USAGE_DEFAULT, (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET, 0);
        ID3D11Texture2D* target;
        Check(device->CreateTexture2D(&targetDescription, null, &target));
        resources.Add((nint)target);
        ID3D11RenderTargetView* targetView;
        Check(device->CreateRenderTargetView((ID3D11Resource*)target, null, &targetView));
        resources.Add((nint)targetView);

        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1 };

        context->ClearState();
        context->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertexShader, null, 0);
        context->PSSetShader(ps, null, 0);
        fixed (ID3D11ShaderResourceView** v = views)
            context->PSSetShaderResources(0, (uint)views.Length, v);
        fixed (ID3D11SamplerState** s = samplers)
            context->PSSetSamplers(0, (uint)samplers.Length, s);
        foreach (var (slot, buffer) in buffers)
        {
            var b = (ID3D11Buffer*)buffer;
            context->PSSetConstantBuffers(slot, 1, &b);
        }

        context->OMSetRenderTargets(1, &targetView, null);
        context->RSSetViewports(1, &viewport);
        context->Draw(3, 0);

        return ReadBack(target, width, height, resources);
    }

    private float[][] ReadBack(ID3D11Texture2D* target, uint width, uint height, List<nint> resources)
    {
        var description = TextureDescription(width, height, D3D11_USAGE.D3D11_USAGE_STAGING, 0, (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
        ID3D11Texture2D* staging;
        Check(device->CreateTexture2D(&description, null, &staging));
        resources.Add((nint)staging);
        context->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)target);

        D3D11_MAPPED_SUBRESOURCE mapped;
        Check(context->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped));
        var values = new float[width * height * 4];
        for (var y = 0; y < height; y++)
            new ReadOnlySpan<float>((byte*)mapped.pData + y * mapped.RowPitch, (int)width * 4).CopyTo(values.AsSpan((int)(y * width * 4)));
        context->Unmap((ID3D11Resource*)staging, 0);
        return values.Chunk(4).ToArray();
    }

    private ID3D11ShaderResourceView* CreateTexture(float[][] pixels, int rows, List<nint> resources)
    {
        var data = pixels.SelectMany(p => p).ToArray();
        var description = TextureDescription((uint)(pixels.Length / rows), (uint)rows, D3D11_USAGE.D3D11_USAGE_IMMUTABLE, (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE, 0);
        fixed (float* p = data)
            return CreateView(&description, p, (uint)(data.Length / rows * sizeof(float)), resources);
    }

    // Une rangée d'octets, comme les courbes de filtre (A8_UNORM pour le jeu, R8_UNORM pour le plugin).
    private ID3D11ShaderResourceView* CreateByteTexture(byte[] values, DXGI_FORMAT format, List<nint> resources)
    {
        var description = TextureDescription((uint)values.Length, 1, D3D11_USAGE.D3D11_USAGE_IMMUTABLE, (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE, 0);
        description.Format = format;
        fixed (byte* p = values)
            return CreateView(&description, p, (uint)values.Length, resources);
    }

    private ID3D11ShaderResourceView* CreateView(D3D11_TEXTURE2D_DESC* description, void* data, uint pitch, List<nint> resources)
    {
        ID3D11Texture2D* texture;
        var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data, SysMemPitch = pitch };
        Check(device->CreateTexture2D(description, &initial, &texture));
        resources.Add((nint)texture);
        ID3D11ShaderResourceView* view;
        Check(device->CreateShaderResourceView((ID3D11Resource*)texture, null, &view));
        resources.Add((nint)view);
        return view;
    }

    private ID3D11SamplerState* CreateSampler(D3D11_FILTER filter, List<nint> resources)
    {
        var description = new D3D11_SAMPLER_DESC
        {
            Filter = filter,
            AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MaxLOD = float.MaxValue,
        };
        ID3D11SamplerState* sampler;
        Check(device->CreateSamplerState(&description, &sampler));
        resources.Add((nint)sampler);
        return sampler;
    }

    private nint CreateConstantBuffer(void* data, uint size, List<nint> resources)
    {
        var description = new D3D11_BUFFER_DESC { ByteWidth = size, Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER };
        var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data };
        ID3D11Buffer* buffer;
        Check(device->CreateBuffer(&description, &initial, &buffer));
        resources.Add((nint)buffer);
        return (nint)buffer;
    }

    private static D3D11_TEXTURE2D_DESC TextureDescription(uint width, uint height, D3D11_USAGE usage, uint bindFlags, uint cpuAccess) => new()
    {
        Width = width,
        Height = height,
        MipLevels = 1,
        ArraySize = 1,
        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
        SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
        Usage = usage,
        BindFlags = bindFlags,
        CPUAccessFlags = cpuAccess,
    };

    private static void Release(List<nint> resources)
    {
        foreach (var resource in resources)
            ((IUnknown*)resource)->Release();
    }

    private static void Check(HRESULT result)
    {
        if (result.FAILED)
            Marshal.ThrowExceptionForHR(result);
    }
}

/// <summary>Ce que le jeu donne à son tonemap en plus de l'image : sa courbe et ses deux tampons de constantes.</summary>
internal sealed class ToneMapInputs
{
    public required float[][] Curve { get; init; }

    /// <summary>cCommonTexParam (b0) : y est l'exposition.</summary>
    public required Vector4 Common { get; init; }

    /// <summary>cToneMapParam (b1) : [0].z échelle de la luminosité, [1].xy plage de la courbe.</summary>
    public required Vector4[] ToneMapParams { get; init; }

    /// <summary>
    /// Une courbe plausible : un facteur qui baisse quand la luminosité monte, un peu différent par couleur comme celle
    /// du jeu peut l'être. Les deux shaders reçoivent la même, c'est ce qui compte pour les comparer.
    /// </summary>
    public static ToneMapInputs Typical()
    {
        const int size = 256;
        var curve = Enumerable.Range(0, size).Select(i =>
        {
            var x = i / (size - 1f);
            var factor = 1f / (1f + 0.6f * x);
            return new[] { factor, factor * 0.98f, factor * 0.96f, 1f };
        }).ToArray();

        return new ToneMapInputs
        {
            Curve = curve,
            Common = new Vector4(0f, 1f, 0f, 0f),
            ToneMapParams = [new(0f, 0f, 0.5f, 0f), new(0.5f / size, 1f - 0.5f / size, 0f, 0f)],
        };
    }
}
