using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FlashColor;

/// <summary>Un paramètre d'un shader du jeu : tampon de constantes, échantillonneur ou texture.</summary>
internal readonly record struct ShaderResource(string Name, uint Id, ushort Slot, ushort Size);

/// <summary>
/// Fichier .shcd du jeu (un seul shader) : un en-tête « ShCd » qui décrit ses paramètres, puis le code compilé (DXBC).
/// Format relevé sur shader/sm5/posteffect/ToneMapping.shcd.
/// </summary>
internal sealed class ShaderCodeFile
{
    private const int ResourceSize = 16;

    private ShaderCodeFile(byte[] bytecode, ShaderResource[] constantBuffers, ShaderResource[] samplers, ShaderResource[] textures)
    {
        Bytecode = bytecode;
        ConstantBuffers = constantBuffers;
        Samplers = samplers;
        Textures = textures;
        Crc32 = FlashColor.Crc32.Compute(bytecode);
    }

    /// <summary>Code compilé (DXBC), tel que le jeu le donne à DirectX.</summary>
    public byte[] Bytecode { get; }

    /// <summary>Empreinte du code compilé : elle change dès que le jeu modifie le shader.</summary>
    public uint Crc32 { get; }

    public IReadOnlyList<ShaderResource> ConstantBuffers { get; }

    public IReadOnlyList<ShaderResource> Samplers { get; }

    public IReadOnlyList<ShaderResource> Textures { get; }

    public static ShaderCodeFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x2C || !data[..4].SequenceEqual("ShCd"u8) || !data.Slice(8, 4).SequenceEqual("DX11"u8))
            throw new FormatException("Not a DirectX 11 .shcd file.");

        var blobsOffset = ReadInt(data, 0x10);
        var stringsOffset = ReadInt(data, 0x14);
        var blobOffset = blobsOffset + ReadInt(data, 0x18);
        var blobSize = ReadInt(data, 0x1C);
        var constantBufferCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x20..]);
        var samplerCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x22..]);
        var uavCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x24..]);
        var textureCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x26..]);
        if (uavCount != 0)
            throw new FormatException("Shaders with UAVs are not supported.");

        if (blobOffset < 0 || blobSize < 32 || blobOffset + blobSize > data.Length)
            throw new FormatException("Shader bytecode out of the file.");

        var bytecode = data.Slice(blobOffset, blobSize).ToArray();
        if (!bytecode.AsSpan(0, 4).SequenceEqual("DXBC"u8) || ReadInt(bytecode, 24) != blobSize)
            throw new FormatException("The shader bytecode is not DXBC.");

        var offset = 0x2C;
        var constantBuffers = ReadResources(data, ref offset, constantBufferCount, stringsOffset);
        var samplers = ReadResources(data, ref offset, samplerCount, stringsOffset);
        var textures = ReadResources(data, ref offset, textureCount, stringsOffset);
        return new ShaderCodeFile(bytecode, constantBuffers, samplers, textures);
    }

    private static ShaderResource[] ReadResources(ReadOnlySpan<byte> data, ref int offset, int count, int stringsOffset)
    {
        var resources = new ShaderResource[count];
        for (var i = 0; i < count; i++, offset += ResourceSize)
        {
            if (offset + ResourceSize > data.Length)
                throw new FormatException("Shader resource table out of the file.");

            var id = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            var nameOffset = stringsOffset + ReadInt(data, offset + 4);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 8)..]);
            var slot = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 12)..]);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 14)..]);
            if (nameOffset < 0 || nameOffset + nameLength > data.Length)
                throw new FormatException("Shader resource name out of the file.");

            resources[i] = new ShaderResource(Encoding.ASCII.GetString(data.Slice(nameOffset, nameLength)), id, slot, size);
        }

        return resources;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }
}
