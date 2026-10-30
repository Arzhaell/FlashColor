using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using TerraFX.Interop.DirectX;

namespace FlashColor;

internal enum ReplacerState
{
    /// <summary>Prêt, mais le jeu n'a pas dessiné d'image avec son tonemap depuis un moment (écran de titre, chargement).</summary>
    Waiting,
    /// <summary>Dalamud n'a pas encore pu brancher FlashColor sur le jeu : nouvel essai bientôt.</summary>
    Hooking,
    /// <summary>Le tonemap du jeu est remplacé en ce moment.</summary>
    Active,
    /// <summary>Le shader du jeu n'est plus celui pour lequel FlashColor est écrit (mise à jour du jeu).</summary>
    GameShaderChanged,
    /// <summary>Arrêté après une erreur (détail dans dalamud.log).</summary>
    Failed,
}

/// <summary>
/// Met le shader de FlashColor à la place du tonemap du jeu (ToneMapping.shcd).
/// Le jeu active ses shaders par <see cref="ImmediateContext.SetPixelShader"/>, qui appelle tout de suite
/// <c>PSSetShader</c> de DirectX et retient le shader actif (vérifié dans le code du jeu). On laisse faire le jeu, puis,
/// si c'est le tonemap, on active notre shader à la place et on ajoute nos réglages dans l'emplacement b13 (et la courbe
/// du filtre de la GPose choisi en t2 / s2). Si c'est le filtre de couleurs du jeu (ColorFilter_DarkBlend.shcd) et qu'un
/// filtre de la GPose est choisi, on le rend neutre (force 0 dans son tampon b2) : comme en GPose, le filtre choisi
/// remplace celui de la zone. Tout se passe sur le fil de rendu du jeu ; ce qu'on a lié est remis au shader suivant.
/// </summary>
internal sealed unsafe class ToneMapReplacer : IDisposable
{
    public const string GameShaderPath = "shader/sm5/posteffect/ToneMapping.shcd";

    public const string ColorFilterShaderPath = "shader/sm5/posteffect/ColorFilter_DarkBlend.shcd";

    /// <summary>Empreinte du ToneMapping.shcd pour lequel notre shader est écrit (relevée le 2026-10-14, jeu 2026.10.06).</summary>
    public const uint SupportedCrc = 0x85E777EF;

    private const uint ParamsSlot = 13;
    private const uint FilterCurveSlot = 2;

    // Le tampon cLutParam du filtre de couleurs du jeu (b2) : lecture de sa courbe au centre de ses 256 cases (relevé en
    // jeu), et une force de 0, qui laisse l'image telle quelle.
    private const uint GameFilterParamsSlot = 2;
    private static readonly float[] NeutralGameFilter = [255f / 256f, 0.5f / 256f, 0f, 0f];

    // Sans tonemap depuis ce délai, on considère que le jeu n'affiche plus d'image (écran de titre, chargement).
    private const long ActiveTimeoutMs = 2000;

    // Pour se brancher, Dalamud range un petit raccord dans de la mémoire libre proche de notre fonction ; il arrive qu'il
    // n'y en ait pas pendant un moment (« Unable to find memory location to fit MemoryBuffer », relevé le 2026-10-15, au
    // hasard d'un rechargement). On réessaie alors de plus en plus espacé, jusqu'à 30 s, au lieu d'abandonner.
    private const long FirstHookRetryMs = 2000;
    private const long MaxHookRetryMs = 30000;

    private delegate void SetPixelShaderDelegate(ImmediateContext* context, PixelShader* shader, void** constantBuffers);

    private Hook<SetPixelShaderDelegate>? hook;
    private nint hookAddress;
    private int hookFailures;
    private long nextHookTry;
    private readonly byte[] bytecode = [];
    private readonly bool gameShaderChanged;
    private readonly ShaderPrint? toneMapPrint;
    private readonly ShaderPrint? colorFilterPrint;
    private readonly object creationLock = new();

    private ShaderParams current;
    private ShaderParams[]? pendingParams;

    // Filtre de la GPose des réglages qui sont dans le tampon du shader (0 : aucun) ; seulement sur le fil de rendu.
    private uint activeFilterRow;
    private ID3D11PixelShader* pixelShader;
    private ID3D11Buffer* paramsBuffer;
    private ID3D11SamplerState* curveSampler;
    private ID3D11Buffer* neutralGameFilter;

    // Courbes des filtres de la GPose déjà envoyées à la carte graphique (une texture de 256 valeurs par filtre).
    private readonly Dictionary<uint, nint> curves = [];

    // Contexte où nos réglages occupent b13 (et la courbe t2 / s2), et ce que le jeu y avait.
    private ImmediateContext* boundContext;
    private ID3D11Buffer* savedBuffer;
    private bool curveBound;
    private ID3D11ShaderResourceView* savedCurveView;
    private ID3D11SamplerState* savedCurveSampler;

    // Contexte où le filtre de couleurs du jeu est rendu neutre, et son tampon b2.
    private ImmediateContext* neutralizedContext;
    private ID3D11Buffer* savedGameFilterBuffer;

    private bool resourcesTried;
    private long lastReplacedTick;
    private int detoursRunning;
    private volatile bool failed;

    public ToneMapReplacer(byte[]? gameFile, byte[]? colorFilterFile, ShaderParams initial)
    {
        SetParams(initial);
        try
        {
            if (gameFile == null)
                throw new InvalidOperationException($"{GameShaderPath} was not found in the game files.");

            var gameShader = ShaderCodeFile.Parse(gameFile);
            GameShaderCrc = gameShader.Crc32;
            if (gameShader.Crc32 != SupportedCrc)
            {
                gameShaderChanged = true;
                Plugin.Log.Warning($"The game's tone mapping shader changed (0x{gameShader.Crc32:X8}, expected 0x{SupportedCrc:X8}): FlashColor stays off until it is updated.");
                return;
            }

            toneMapPrint = new ShaderPrint("tone mapping", gameShader);
            bytecode = ShaderCompiler.Compile(ShaderCompiler.LoadToneMappingSource(), "main", "ps_5_0");

            // Sans lui, les filtres de la GPose s'ajoutent à celui de la zone au lieu de le remplacer.
            if (colorFilterFile != null)
                colorFilterPrint = new ShaderPrint("color filter", ShaderCodeFile.Parse(colorFilterFile));
            else
                Plugin.Log.Warning($"{ColorFilterShaderPath} was not found: GPose filters add up to the zone's filter.");

            var address = (nint)ImmediateContext.Addresses.SetPixelShader.Value;
            if (address == 0)
                throw new InvalidOperationException("ImmediateContext.SetPixelShader was not found in the game.");

            hookAddress = address;
            TryHook();
        }
        catch (Exception e)
        {
            Fail(e, "Could not start");
        }
    }

    /// <summary>Si le branchement sur le jeu a échoué, le réessaie quand c'est l'heure (fil principal, à chaque image).</summary>
    public void RetryHook()
    {
        if (hook == null && hookAddress != 0 && !failed && Environment.TickCount64 >= nextHookTry)
            TryHook();
    }

    /// <summary>Remplacer le tonemap ; faux : l'image est celle du jeu.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Montrer l'image d'origine le temps d'une comparaison, sans changer <see cref="Enabled"/>.</summary>
    public bool Bypass { get; set; }

    public uint GameShaderCrc { get; }

    public ReplacerState State
    {
        get
        {
            if (gameShaderChanged)
                return ReplacerState.GameShaderChanged;
            if (failed)
                return ReplacerState.Failed;
            if (hook == null)
                return ReplacerState.Hooking;

            var last = Volatile.Read(ref lastReplacedTick);
            return last != 0 && Environment.TickCount64 - last < ActiveTimeoutMs ? ReplacerState.Active : ReplacerState.Waiting;
        }
    }

    /// <summary>Le tonemap du jeu a été reconnu au moins une fois.</summary>
    public bool EverReplaced => Volatile.Read(ref lastReplacedTick) != 0;

    /// <summary>Nouveaux réglages, envoyés au shader à son prochain passage (sur le fil de rendu).</summary>
    public void SetParams(ShaderParams parameters)
    {
        current = parameters;
        Interlocked.Exchange(ref pendingParams, [parameters]);
    }

    public void Dispose()
    {
        Enabled = false;
        hook?.Dispose();

        // Un passage peut être en cours sur le fil de rendu : on l'attend avant de libérer ce qu'il utilise.
        var wait = Stopwatch.StartNew();
        while (Volatile.Read(ref detoursRunning) > 0 && wait.ElapsedMilliseconds < 500)
            Thread.Sleep(1);

        // DirectX garde ses propres références sur ce qui est encore actif : libérer les nôtres est sans risque.
        foreach (var view in curves.Values)
            ((ID3D11ShaderResourceView*)view)->Release();
        curves.Clear();
        Release(ref savedBuffer);
        Release(ref savedCurveView);
        Release(ref savedCurveSampler);
        Release(ref savedGameFilterBuffer);
        Release(ref neutralGameFilter);
        Release(ref curveSampler);
        Release(ref paramsBuffer);
        Release(ref pixelShader);
    }

    private void TryHook()
    {
        try
        {
            // Le crochet est rangé avant d'être activé : le détour s'en sert dès son premier passage.
            hook = Plugin.GameInterop.HookFromAddress<SetPixelShaderDelegate>(hookAddress, Detour);
            hook.Enable();
            Plugin.Log.Information(hookFailures == 0
                ? $"Ready: waiting for the game's tone mapping shader ({toneMapPrint!.Size} bytes)."
                : $"Ready after {hookFailures + 1} tries: waiting for the game's tone mapping shader ({toneMapPrint!.Size} bytes).");
        }
        catch (Exception e)
        {
            hook?.Dispose();
            hook = null;
            hookFailures++;
            var delay = Math.Min(FirstHookRetryMs << Math.Min(hookFailures - 1, 4), MaxHookRetryMs);
            nextHookTry = Environment.TickCount64 + delay;
            Plugin.Log.Warning($"Could not hook the game yet (try {hookFailures}: {e.Message}); trying again in {delay / 1000} s.");
        }
    }

    private void Detour(ImmediateContext* context, PixelShader* shader, void** constantBuffers)
    {
        Interlocked.Increment(ref detoursRunning);
        try
        {
            // Ce qu'on a lié n'a servi qu'au shader précédent : on remet ce que le jeu avait.
            if (boundContext == context || neutralizedContext == context)
            {
                try
                {
                    RestoreSlots(context);
                }
                catch (Exception e)
                {
                    boundContext = null;
                    neutralizedContext = null;
                    Fail(e, "Stopped after an error while restoring the game's constant buffer");
                }
            }

            // Le jeu active toujours son shader, quoi qu'il arrive de notre côté.
            hook!.Original(context, shader, constantBuffers);

            try
            {
                if (Enabled && !Bypass && !failed && toneMapPrint!.Matches(shader))
                    Replace(context);
                else if (Enabled && !Bypass && !failed && activeFilterRow != 0 && colorFilterPrint != null && colorFilterPrint.Matches(shader))
                    NeutralizeGameFilter(context);
            }
            catch (Exception e)
            {
                Fail(e, "Stopped after an error while replacing the tone mapping");
            }
        }
        finally
        {
            Interlocked.Decrement(ref detoursRunning);
        }
    }

    private void Replace(ImmediateContext* context)
    {
        var d3d = (ID3D11DeviceContext*)context->D3D11DeviceContext_2;
        if (d3d == null)
            return;

        if (pixelShader == null)
        {
            lock (creationLock)
            {
                if (!resourcesTried)
                {
                    resourcesTried = true;
                    CreateResources(d3d);
                }
            }

            if (pixelShader == null)
                return;
        }

        UploadParams(d3d);
        d3d->PSSetShader(pixelShader, null, 0);

        ID3D11Buffer* previous = null;
        d3d->PSGetConstantBuffers(ParamsSlot, 1, &previous);
        savedBuffer = previous;
        var buffer = paramsBuffer;
        d3d->PSSetConstantBuffers(ParamsSlot, 1, &buffer);

        // La courbe du filtre de la GPose choisi (le jeu ne lie rien en t2 / s2 pour son tonemap).
        var curve = activeFilterRow != 0 ? CurveView(d3d, activeFilterRow) : null;
        if (curve != null)
        {
            ID3D11ShaderResourceView* view = null;
            ID3D11SamplerState* sampler = null;
            d3d->PSGetShaderResources(FilterCurveSlot, 1, &view);
            d3d->PSGetSamplers(FilterCurveSlot, 1, &sampler);
            savedCurveView = view;
            savedCurveSampler = sampler;
            sampler = curveSampler;
            d3d->PSSetShaderResources(FilterCurveSlot, 1, &curve);
            d3d->PSSetSamplers(FilterCurveSlot, 1, &sampler);
            curveBound = true;
        }

        boundContext = context;

        if (Interlocked.Exchange(ref lastReplacedTick, Environment.TickCount64) == 0)
            Plugin.Log.Information("The game's tone mapping is now replaced.");
    }

    // Le jeu active son filtre de couleurs (filtre de la zone) : force 0, l'image passe telle quelle.
    private void NeutralizeGameFilter(ImmediateContext* context)
    {
        var d3d = (ID3D11DeviceContext*)context->D3D11DeviceContext_2;
        if (d3d == null || neutralGameFilter == null)
            return;

        ID3D11Buffer* previous = null;
        d3d->PSGetConstantBuffers(GameFilterParamsSlot, 1, &previous);
        savedGameFilterBuffer = previous;
        var buffer = neutralGameFilter;
        d3d->PSSetConstantBuffers(GameFilterParamsSlot, 1, &buffer);
        neutralizedContext = context;
    }

    private void RestoreSlots(ImmediateContext* context)
    {
        var d3d = (ID3D11DeviceContext*)context->D3D11DeviceContext_2;
        if (boundContext == context)
        {
            var previous = savedBuffer;
            d3d->PSSetConstantBuffers(ParamsSlot, 1, &previous);
            Release(ref savedBuffer);
            if (curveBound)
            {
                var view = savedCurveView;
                var sampler = savedCurveSampler;
                d3d->PSSetShaderResources(FilterCurveSlot, 1, &view);
                d3d->PSSetSamplers(FilterCurveSlot, 1, &sampler);
                Release(ref savedCurveView);
                Release(ref savedCurveSampler);
                curveBound = false;
            }

            boundContext = null;
        }

        if (neutralizedContext == context)
        {
            var previous = savedGameFilterBuffer;
            d3d->PSSetConstantBuffers(GameFilterParamsSlot, 1, &previous);
            Release(ref savedGameFilterBuffer);
            neutralizedContext = null;
        }
    }

    // La courbe d'un filtre de la GPose, envoyée une fois à la carte graphique (texture de 256 × 1).
    private ID3D11ShaderResourceView* CurveView(ID3D11DeviceContext* d3d, uint row)
    {
        if (curves.TryGetValue(row, out var known))
            return (ID3D11ShaderResourceView*)known;

        var filter = GameFilters.Find(row);
        if (filter == null)
            return null;

        ID3D11Device* device = null;
        d3d->GetDevice(&device);
        try
        {
            var curve = filter.Curve;
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)curve.Length,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
                Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            };
            ID3D11Texture2D* texture;
            fixed (byte* data = curve)
            {
                var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data, SysMemPitch = (uint)curve.Length };
                ThrowIfFailed(device->CreateTexture2D(&description, &initial, &texture), "CreateTexture2D (filter curve)");
            }

            ID3D11ShaderResourceView* view;
            var result = device->CreateShaderResourceView((ID3D11Resource*)texture, null, &view);
            texture->Release();
            ThrowIfFailed(result, "CreateShaderResourceView (filter curve)");
            curves[row] = (nint)view;
            return view;
        }
        finally
        {
            device->Release();
        }
    }

    private void CreateResources(ID3D11DeviceContext* d3d)
    {
        ID3D11Device* device = null;
        d3d->GetDevice(&device);
        try
        {
            ID3D11PixelShader* shader;
            fixed (byte* code = bytecode)
                ThrowIfFailed(device->CreatePixelShader(code, (nuint)bytecode.Length, null, &shader), "CreatePixelShader");
            pixelShader = shader;

            var initial = current;
            paramsBuffer = CreateBuffer(device, &initial, ShaderParams.Size, D3D11_USAGE.D3D11_USAGE_DYNAMIC);
            activeFilterRow = FilterRow(initial);
            fixed (float* neutral = NeutralGameFilter)
                neutralGameFilter = CreateBuffer(device, neutral, 16, D3D11_USAGE.D3D11_USAGE_IMMUTABLE);

            var samplerDescription = new D3D11_SAMPLER_DESC
            {
                Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                MaxLOD = float.MaxValue,
            };
            ID3D11SamplerState* sampler;
            ThrowIfFailed(device->CreateSamplerState(&samplerDescription, &sampler), "CreateSamplerState");
            curveSampler = sampler;
        }
        catch
        {
            // Rien à moitié : sans toutes ses ressources, le tonemap reste celui du jeu.
            Release(ref curveSampler);
            Release(ref neutralGameFilter);
            Release(ref paramsBuffer);
            Release(ref pixelShader);
            throw;
        }
        finally
        {
            device->Release();
        }
    }

    private static ID3D11Buffer* CreateBuffer(ID3D11Device* device, void* data, uint size, D3D11_USAGE usage)
    {
        var description = new D3D11_BUFFER_DESC
        {
            ByteWidth = size,
            Usage = usage,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
            CPUAccessFlags = usage == D3D11_USAGE.D3D11_USAGE_DYNAMIC ? (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE : 0,
        };
        var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data };
        ID3D11Buffer* buffer;
        ThrowIfFailed(device->CreateBuffer(&description, &initial, &buffer), "CreateBuffer");
        return buffer;
    }

    private void UploadParams(ID3D11DeviceContext* d3d)
    {
        var pending = Interlocked.Exchange(ref pendingParams, null);
        if (pending == null)
            return;

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (d3d->Map((ID3D11Resource*)paramsBuffer, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped) < 0)
        {
            // Réessayé au prochain passage, sauf si des réglages plus récents sont arrivés entre-temps.
            Interlocked.CompareExchange(ref pendingParams, pending, null);
            return;
        }

        *(ShaderParams*)mapped.pData = pending[0];
        d3d->Unmap((ID3D11Resource*)paramsBuffer, 0);
        activeFilterRow = FilterRow(pending[0]);
    }

    private static uint FilterRow(ShaderParams parameters) => parameters.Filter.X > 0f ? (uint)parameters.Filter.Y : 0;

    private void Fail(Exception e, string message)
    {
        failed = true;
        Plugin.Log.Error(e, $"{message}: the game's image is left untouched.");
    }

    private static void Release<T>(ref T* item)
        where T : unmanaged
    {
        if (item != null)
            ((TerraFX.Interop.Windows.IUnknown*)item)->Release();
        item = null;
    }

    private static void ThrowIfFailed(int result, string call)
    {
        if (result < 0)
            throw new InvalidOperationException($"{call} failed: 0x{result:X8}");
    }
}
