using System;
using System.IO;
using System.Text;
using TerraFX.Interop.DirectX;

namespace FlashColor;

/// <summary>Compile le shader de FlashColor avec le compilateur de Windows (d3dcompiler_47.dll, présent sur Windows 10 et 11).</summary>
internal static unsafe class ShaderCompiler
{
    private const uint OptimizationLevel3 = 1 << 15;

    public static string LoadToneMappingSource()
    {
        using var stream = typeof(ShaderCompiler).Assembly.GetManifestResourceStream("FlashColor.ToneMapping.hlsl")
            ?? throw new InvalidOperationException("The tone mapping shader is missing from the plugin.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Code compilé (DXBC) ; une erreur de compilation lève une exception avec le message du compilateur.</summary>
    public static byte[] Compile(string source, string entryPoint, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var entryBytes = Encoding.ASCII.GetBytes(entryPoint + '\0');
        var targetBytes = Encoding.ASCII.GetBytes(target + '\0');
        var nameBytes = Encoding.ASCII.GetBytes("FlashColor\0");

        ID3DBlob* code = null;
        ID3DBlob* errors = null;
        try
        {
            int result;
            fixed (byte* src = sourceBytes)
            fixed (byte* entry = entryBytes)
            fixed (byte* tgt = targetBytes)
            fixed (byte* name = nameBytes)
            {
                result = DirectX.D3DCompile(src, (nuint)sourceBytes.Length, (sbyte*)name, null, null, (sbyte*)entry, (sbyte*)tgt, OptimizationLevel3, 0, &code, &errors);
            }

            if (result < 0 || code == null)
            {
                var message = errors != null
                    ? Encoding.ASCII.GetString((byte*)errors->GetBufferPointer(), (int)errors->GetBufferSize()).TrimEnd('\0')
                    : $"0x{result:X8}";
                throw new InvalidOperationException($"Shader compilation failed: {message}");
            }

            return new ReadOnlySpan<byte>(code->GetBufferPointer(), (int)code->GetBufferSize()).ToArray();
        }
        finally
        {
            if (code != null)
                code->Release();
            if (errors != null)
                errors->Release();
        }
    }
}
