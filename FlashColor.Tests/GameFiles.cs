using Lumina;

namespace FlashColor.Tests;

/// <summary>
/// Lecture seule des fichiers du jeu installé. Sans le jeu, les tests qui en ont besoin échouent, sauf avec la variable
/// d'environnement FLASHCOLOR_TESTS_WITHOUT_GAME=1 (machine sans le jeu, comme GitHub Actions) : ils ne vérifient alors rien.
/// </summary>
internal static class GameFiles
{
    private const string SqPackPath = @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    private static readonly Lazy<GameData?> Data = new(() =>
    {
        if (!Directory.Exists(SqPackPath))
        {
            if (Environment.GetEnvironmentVariable("FLASHCOLOR_TESTS_WITHOUT_GAME") == "1")
                return null;
            throw new InvalidOperationException($"Game not found in {SqPackPath} (set FLASHCOLOR_TESTS_WITHOUT_GAME=1 to skip).");
        }

        return new GameData(SqPackPath, new LuminaOptions { PanicOnSheetChecksumMismatch = false });
    });

    private static readonly Lazy<ShaderCodeFile?> ToneMappingFile = new(() => Shader(ToneMapReplacer.GameShaderPath));

    private static readonly Lazy<ShaderCodeFile?> ColorFilterFile = new(() => Shader(ToneMapReplacer.ColorFilterShaderPath));

    /// <summary>ToneMapping.shcd du jeu, ou null si on a demandé de se passer du jeu.</summary>
    public static ShaderCodeFile? ToneMapping => ToneMappingFile.Value;

    /// <summary>ColorFilter_DarkBlend.shcd du jeu (son filtre de couleurs), ou null si on a demandé de se passer du jeu.</summary>
    public static ShaderCodeFile? ColorFilter => ColorFilterFile.Value;

    private static ShaderCodeFile? Shader(string path)
    {
        if (Data.Value is not { } data)
            return null;

        var file = data.GetFile(path) ?? throw new InvalidOperationException($"{path} not found in the game files.");
        return ShaderCodeFile.Parse(file.Data);
    }
}
