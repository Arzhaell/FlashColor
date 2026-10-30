using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;

namespace FlashColor;

/// <summary>
/// Lectures dans la mémoire du jeu hors des structures décrites par ClientStructs : par Windows, qui répond « illisible »
/// au lieu de faire planter le jeu si l'adresse est mauvaise (une lecture directe fausse le fait planter, relevé le
/// 2026-10-15).
/// </summary>
internal static unsafe class GameMemory
{
    // Dans EnvManager, après ColorFilterOverride (0x3D0, 92 octets) : vaut 2 tant qu'un filtre de la GPose autre que
    // « Standard » est choisi, 0 sinon (relevé en GPose le 2026-10-15 sur les 25 filtres).
    private const int GPoseFilterFlagOffset = 0x458;

    /// <summary>Un filtre de la GPose (autre que « Standard ») est choisi dans le jeu.</summary>
    public static bool GPoseFilterChosen()
    {
        var env = (byte*)EnvManager.Instance();
        return env != null && TryReadSingle(env + GPoseFilterFlagOffset, out var flag) && flag == 2f;
    }

    private static bool TryReadSingle(byte* address, out float value)
    {
        float result;
        var ok = ReadProcessMemory(GetCurrentProcess(), (nint)address, &result, sizeof(float), out var read) && read == sizeof(float);
        value = ok ? result : 0f;
        return ok;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, void* buffer, nuint size, out nuint read);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
