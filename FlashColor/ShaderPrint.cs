using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;

namespace FlashColor;

/// <summary>
/// Empreinte d'un shader du jeu, lue dans son fichier .shcd, pour le reconnaître en mémoire.
/// Le jeu ne garde ni le code compilé en mémoire, ni les identifiants du fichier (0x61 au lieu de 0x9262F48B), et il
/// compte les textures avec les échantillonneurs (relevé en jeu le 2026-10-14). En plus de la taille du code, on
/// compare donc les tampons de constantes (emplacement, taille en registres) et le nombre d'échantillonneurs + textures.
/// </summary>
internal sealed unsafe class ShaderPrint
{
    private readonly ShaderResource[] buffers;
    private readonly int samplersAndTextures;

    // Verdict pour chaque shader du jeu de la bonne taille, établi une fois (et écrit dans dalamud.log).
    private readonly Dictionary<nint, bool> verdicts = [];

    public ShaderPrint(string name, ShaderCodeFile file)
    {
        Name = name;
        Size = file.Bytecode.Length;
        buffers = file.ConstantBuffers.ToArray();
        samplersAndTextures = file.Samplers.Count + file.Textures.Count;
    }

    public string Name { get; }

    public int Size { get; }

    public bool Matches(PixelShader* shader)
    {
        if (shader == null || shader->DxbcBlobSize != Size)
            return false;

        lock (verdicts)
        {
            if (!verdicts.TryGetValue((nint)shader, out var verdict))
            {
                verdict = Identify(shader);
                verdicts[(nint)shader] = verdict;
            }

            return verdict;
        }
    }

    private bool Identify(PixelShader* shader)
    {
        var found = new List<string>();
        var matches = shader->ConstantBufferCount == buffers.Length && shader->SamplerCount == samplersAndTextures;
        for (var i = 0; i < shader->ConstantBufferCount; i++)
        {
            var entry = shader->ConstantBuffers[i];
            found.Add($"slot {entry.Slot} size {entry.Size}");
            matches &= Array.Exists(buffers, c => c.Slot == entry.Slot && c.Size == entry.Size);
        }

        Plugin.Log.Information($"Shader of {shader->DxbcBlobSize} bytes at 0x{(nint)shader:X} ({string.Join(", ", found)}; {shader->SamplerCount} samplers and textures): {(matches ? $"the game's {Name}" : $"not the {Name}")}.");
        return matches;
    }
}
