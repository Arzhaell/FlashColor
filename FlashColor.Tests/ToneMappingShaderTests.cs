using System.Text;
using TerraFX.Interop.DirectX;

namespace FlashColor.Tests;

/// <summary>
/// Le shader de FlashColor face au vrai shader du jeu (lu dans ses fichiers), sur les mêmes pixels, sur WARP.
/// Les pixels arrivent comme dans le jeu : lumière en « gamma 2 », au-delà de 1 pour les lumières fortes.
/// </summary>
public sealed class ToneMappingShaderTests : IDisposable
{
    private const int Flame = 13;

    private static readonly float[][] Pixels =
    [
        // Gris, du noir aux lumières fortes.
        Pixel(0f), Pixel(0.05f), Pixel(0.1f), Pixel(0.2f), Pixel(0.3f), Pixel(0.45f), Pixel(0.6f), Pixel(0.75f),
        Pixel(0.9f), Pixel(1f), Pixel(1.2f), Pixel(1.5f), Pixel(2f),
        // Flamme, braise, ciel, rouge vif, feuillage.
        Pixel(1.8f, 1.1f, 0.4f), Pixel(1.3f, 0.8f, 0.25f), Pixel(0.4f, 0.6f, 0.9f), Pixel(1f, 0.1f, 0.05f), Pixel(0.2f, 0.45f, 0.15f),
    ];

    private static readonly Lazy<byte[]> OurShader = new(() => ShaderCompiler.Compile(ShaderCompiler.LoadToneMappingSource(), "main", "ps_5_0"));

    private readonly WarpRenderer renderer = new();
    private readonly ToneMapInputs inputs = ToneMapInputs.Typical();

    public void Dispose() => renderer.Dispose();

    [Fact]
    public void NeutralSettingsGiveTheGameImage()
    {
        var game = GameFiles.ToneMapping;
        if (game == null)
            return;

        var expected = renderer.Run(game.Bytecode, Pixels, inputs, default);
        var actual = Run(new GradeSettings { HighlightRolloff = false });

        for (var i = 0; i < Pixels.Length; i++)
            for (var c = 0; c < 4; c++)
                Assert.True(MathF.Abs(expected[i][c] - actual[i][c]) <= 1e-5f * MathF.Max(1f, MathF.Abs(expected[i][c])),
                    $"Pixel {i}, channel {c}: game {expected[i][c]}, FlashColor {actual[i][c]}");
    }

    [Fact]
    public void GameClipsTheFlameButRolloffKeepsItsColor()
    {
        var game = GameFiles.ToneMapping;
        if (game == null)
            return;

        // Couleur réelle de la flamme après la courbe du jeu, avant toute coupure.
        var unclipped = Linear(Run(new GradeSettings { HighlightRolloff = false })[Flame]);
        Assert.True(unclipped[0] > 1f, "The test flame must go beyond white.");
        var trueRatio = unclipped[1] / unclipped[0];

        // Le jeu coupe ensuite chaque couleur à 1 (Saturate.shcd) : le rapport vert/rouge change, la flamme jaunit.
        var gameOutput = Linear(renderer.Run(game.Bytecode, Pixels, inputs, default)[Flame].Select(Clamp01).ToArray());
        Assert.True(gameOutput[1] / gameOutput[0] - trueRatio > 0.2f, $"Game ratio {gameOutput[1] / gameOutput[0]}, true {trueRatio}");

        var softened = Linear(Run(new GradeSettings { KeepHighlightColor = 1f, KeepDetails = 0f })[Flame]);
        Assert.True(softened[0] < 1f, $"Red {softened[0]}");
        Assert.Equal(trueRatio, softened[1] / softened[0], 3);
        Assert.Equal(unclipped[2] / unclipped[0], softened[2] / softened[0], 3);
    }

    [Fact]
    public void StrongSettingsNeverGoBeyondWhite()
    {
        var output = Run(new GradeSettings { Exposure = 0.5f, Contrast = 1.6f, Saturation = 1.5f, Temperature = 40f });

        foreach (var pixel in output)
            for (var c = 0; c < 3; c++)
                Assert.InRange(pixel[c], 0f, 1f);
    }

    [Fact]
    public void StrongestFlashStaysValidAndBelowWhite()
    {
        var parameters = ShaderParams.From(new GradeSettings());
        parameters.Gain *= 256f;
        var output = renderer.Run(OurShader.Value, Pixels, inputs, parameters);

        foreach (var pixel in output)
            for (var c = 0; c < 3; c++)
                Assert.InRange(pixel[c], 0f, 1f);
    }

    [Fact]
    public void LightsBelowTheStartAreUntouched()
    {
        var settings = new GradeSettings { Contrast = 1.3f };
        var without = Run(new GradeSettings { Contrast = 1.3f, HighlightRolloff = false });
        var with = Run(settings);
        var start = settings.RolloffStart;

        for (var i = 0; i < Pixels.Length; i++)
        {
            if (without[i].Take(3).Max() >= start)
                continue;
            for (var c = 0; c < 3; c++)
                Assert.Equal(without[i][c], with[i][c], 6);
        }
    }

    [Fact]
    public void NaturalHighlightsTurnTowardsYellowNotPink()
    {
        // Couleur par couleur, l'orange d'une flamme gagne du vert (vers le jaune) et reste plus rouge que bleu :
        // jamais le rose saumon d'un mélange avec du blanc.
        var kept = Linear(Run(new GradeSettings { KeepHighlightColor = 1f, KeepDetails = 0f })[Flame]);
        var natural = Linear(Run(new GradeSettings { KeepHighlightColor = 0f, KeepDetails = 0f })[Flame]);

        Assert.True(natural[1] / natural[0] > kept[1] / kept[0] + 0.1f, $"Natural {string.Join(", ", natural)}, kept {string.Join(", ", kept)}");
        Assert.True(natural[1] - natural[2] > natural[0] - natural[1], $"Natural {string.Join(", ", natural)}: green should sit closer to red than to blue");
        Assert.All(natural, v => Assert.InRange(v, 0f, 1f));
    }

    [Fact]
    public void FlameTextureSurvivesTheSoftening()
    {
        // Une flamme très lumineuse rayée de fines variations (colonnes alternées) : sans garder les détails,
        // l'adoucissement les écrase ; en les gardant, l'écart entre colonnes voisines reste net.
        const int size = 96;
        var image = Enumerable.Range(0, size * size)
            .Select(i => (i % size) % 2 == 0 ? Pixel(1.6f, 1.0f, 0.35f) : Pixel(2.2f, 1.4f, 0.5f))
            .ToArray();
        float Stripes(GradeSettings settings)
        {
            var output = renderer.Run(OurShader.Value, image, inputs, ShaderParams.From(settings), size);
            var row = size / 2 * size;
            return Enumerable.Range(size / 4, size / 2).Average(x => MathF.Abs(output[row + x][1] - output[row + x + 1][1]));
        }

        var flat = Stripes(new GradeSettings { Contrast = 1.2f, KeepDetails = 0f });
        var detailed = Stripes(new GradeSettings { Contrast = 1.2f, KeepDetails = 1f });
        Assert.True(detailed > 2f * flat, $"Stripes without keeping details {flat}, with {detailed}");
    }

    [Fact]
    public void KeepingDetailsChangesNothingOnFlatAreas()
    {
        const int size = 64;
        var image = Enumerable.Repeat(Pixel(0.6f, 0.45f, 0.3f), size * size).ToArray();
        var flat = renderer.Run(OurShader.Value, image, inputs, ShaderParams.From(new GradeSettings { Contrast = 1.3f, KeepDetails = 0f }), size);
        var kept = renderer.Run(OurShader.Value, image, inputs, ShaderParams.From(new GradeSettings { Contrast = 1.3f, KeepDetails = 1f }), size);

        for (var c = 0; c < 3; c++)
            Assert.Equal(flat[size * size / 2][c], kept[size * size / 2][c], 4);
    }

    [Fact]
    public void ZoneColorsSpareVividColors()
    {
        // Tons moyens bleus à fond : un gris bleuit nettement, une flamme orange beaucoup moins.
        var neutral = Run(new GradeSettings { HighlightRolloff = false, KeepDetails = 0f });
        var tinted = Run(new GradeSettings { HighlightRolloff = false, KeepDetails = 0f, Midtones = new ToneZone { Hue = 240f, Strength = 1f }, Highlights = new ToneZone { Hue = 240f, Strength = 1f } });

        float BlueShift(int i) => Linear(tinted[i])[2] / Linear(tinted[i])[0] / (Linear(neutral[i])[2] / Linear(neutral[i])[0]);
        // Bleu gagné : la flamme (couleur vive) en reçoit moins de la moitié de ce que reçoit le gris.
        Assert.True(BlueShift(7) > 1.5f && BlueShift(Flame) - 1f < 0.5f * (BlueShift(7) - 1f), $"Grey ×{BlueShift(7)}, flame ×{BlueShift(Flame)}");
    }

    [Fact]
    public void ContrastDarkensShadowsAndBrightensLights()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var contrasted = Run(new GradeSettings { Contrast = 1.5f, HighlightRolloff = false });

        Assert.True(contrasted[3][0] < neutral[3][0], "A dark grey should get darker.");
        Assert.True(contrasted[7][0] > neutral[7][0], "A light grey should get lighter.");

        // La teinte ne bouge pas : les rapports entre couleurs restent ceux de l'image neutre (ciel bleu).
        var sky = Linear(neutral[15]);
        var contrastedSky = Linear(contrasted[15]);
        Assert.Equal(sky[2] / sky[0], contrastedSky[2] / contrastedSky[0], 4);
    }

    [Fact]
    public void NoSaturationGivesGreys()
    {
        var output = Run(new GradeSettings { Saturation = 0f, HighlightRolloff = false });

        foreach (var pixel in output)
        {
            Assert.Equal(pixel[0], pixel[1], 5);
            Assert.Equal(pixel[0], pixel[2], 5);
        }
    }

    [Fact]
    public void ShadowColorTintsDarkPixelsMoreThanBrightOnes()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var tinted = Run(new GradeSettings { HighlightRolloff = false, Shadows = new ToneZone { Hue = 240f, Strength = 1f } });

        // Rapport bleu / rouge (en lumière), par rapport à l'image neutre : un gris sombre (0,1) et un gris clair (1,0).
        float Bluer(int i) => Linear(tinted[i])[2] / Linear(tinted[i])[0] / (Linear(neutral[i])[2] / Linear(neutral[i])[0]);
        Assert.True(Bluer(2) > 1.5f, $"Dark grey {Bluer(2)}");
        Assert.True(Bluer(2) > 2f * Bluer(9) - 1f && Bluer(9) < 1.3f, $"Light grey {Bluer(9)}");
    }

    [Fact]
    public void ZoneBrightnessLightensItsZone()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var lighter = Run(new GradeSettings { HighlightRolloff = false, Highlights = new ToneZone { Brightness = 1f } });

        var bright = Linear(lighter[9])[1] / Linear(neutral[9])[1];
        var dark = Linear(lighter[2])[1] / Linear(neutral[2])[1];
        Assert.True(bright > 1.4f && dark < 1.1f, $"Bright ×{bright}, dark ×{dark}");
    }

    [Fact]
    public void VibranceLivensDullColorsMore()
    {
        float[][] pixels = [Pixel(0.5f, 0.45f, 0.4f), Pixel(1f, 0.1f, 0.05f)];
        var neutral = renderer.Run(OurShader.Value, pixels, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false }));
        var vivid = renderer.Run(OurShader.Value, pixels, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false, Vibrance = 1f }));

        float Gain(int i) => Colorfulness(Linear(vivid[i])) / Colorfulness(Linear(neutral[i]));
        Assert.True(Gain(0) > 1.3f && Gain(1) < 1.05f, $"Dull ×{Gain(0)}, vivid ×{Gain(1)}");
    }

    [Fact]
    public void PerColorSaturationOnlyTouchesThatColor()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var settings = new GradeSettings { HighlightRolloff = false };
        settings.Colors[3].Saturation = 0f; // vert
        var output = Run(settings);

        Assert.True(Colorfulness(Linear(output[17])) < 0.2f * Colorfulness(Linear(neutral[17])), "The foliage should turn almost grey.");
        for (var c = 0; c < 3; c++)
            Assert.Equal(neutral[16][c], output[16][c], 5);
    }

    [Fact]
    public void PerColorHueShiftMovesRedTowardsOrange()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var settings = new GradeSettings { HighlightRolloff = false };
        settings.Colors[0].Hue = 30f; // rouge
        var output = Run(settings);

        Assert.True(Linear(output[16])[1] > 2f * Linear(neutral[16])[1], "Red should gain green (orange).");
        for (var c = 0; c < 3; c++)
            Assert.Equal(neutral[15][c], output[15][c], 5);
    }

    [Fact]
    public void PerColorLuminanceDarkensTheSkyOnly()
    {
        var neutral = Run(new GradeSettings { HighlightRolloff = false });
        var settings = new GradeSettings { HighlightRolloff = false };
        settings.Colors[5].Luminance = -1f; // bleu
        var output = Run(settings);

        Assert.True(Linear(output[15])[2] < 0.8f * Linear(neutral[15])[2], "The sky should get darker.");
        for (var c = 0; c < 3; c++)
            Assert.Equal(neutral[16][c], output[16][c], 5);
    }

    [Fact]
    public void SharpnessAddsContrastAtEdgesOnly()
    {
        float[][] edge = [Pixel(0.3f), Pixel(0.3f), Pixel(0.3f), Pixel(0.3f), Pixel(0.6f), Pixel(0.6f), Pixel(0.6f), Pixel(0.6f)];
        var neutral = renderer.Run(OurShader.Value, edge, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false }));
        var sharp = renderer.Run(OurShader.Value, edge, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false, Sharpness = 1f }));

        Assert.True(sharp[3][1] < neutral[3][1] && sharp[4][1] > neutral[4][1], "The edge should get crisper.");
        Assert.Equal(neutral[0][1], sharp[0][1], 5);
        Assert.Equal(neutral[7][1], sharp[7][1], 5);
    }

    [Fact]
    public void ClarityLeavesFlatAreasAlone()
    {
        float[][] flat = Enumerable.Repeat(Pixel(0.4f), 16).ToArray();
        var neutral = renderer.Run(OurShader.Value, flat, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false }));
        var clear = renderer.Run(OurShader.Value, flat, inputs, ShaderParams.From(new GradeSettings { HighlightRolloff = false, Clarity = 1f }));

        for (var i = 0; i < flat.Length; i++)
            Assert.Equal(neutral[i][1], clear[i][1], 4);
    }

    [Fact]
    public void EverythingAtOnceStaysValid()
    {
        var settings = new GradeSettings
        {
            Exposure = 2f, Contrast = 2f, Saturation = 2f, Vibrance = 1f, Temperature = 100f, Tint = -100f,
            Shadows = new ToneZone { Brightness = 1f, Hue = 240f, Strength = 1f },
            Midtones = new ToneZone { Brightness = -1f, Hue = 120f, Strength = 1f },
            Highlights = new ToneZone { Brightness = 1f, Hue = 30f, Strength = 1f },
            Sharpness = 1f, Clarity = -1f,
        };
        foreach (var color in settings.Colors)
        {
            color.Hue = 30f;
            color.Saturation = 2f;
            color.Luminance = 1f;
        }

        foreach (var pixel in Run(settings))
            for (var c = 0; c < 3; c++)
                Assert.InRange(pixel[c], 0f, 1f);
    }

    [Fact]
    public void GPoseFiltersMatchTheGameFilterShader()
    {
        var game = GameFiles.ColorFilter;
        if (game == null)
            return;

        // Pour chaque filtre : notre image sans filtre passée dans le vrai shader de filtre du jeu (après la coupure à 1
        // de Saturate.shcd), face à notre image avec le filtre. Avec des réglages de couleurs en plus, qui s'y ajoutent.
        float[][] pixels = [.. Pixels, .. Enumerable.Range(0, 33).Select(i => Pixel(i / 32f * 1.2f))];
        var grade = new GradeSettings { Contrast = 1.1f, Saturation = 1.2f };
        var unfiltered = renderer.Run(OurShader.Value, pixels, inputs, ShaderParams.From(grade))
            .Select(p => new[] { Clamp01(p[0]), Clamp01(p[1]), Clamp01(p[2]), p[3] }).ToArray();

        foreach (var filter in GameFilters.All)
        {
            grade.GameFilter = filter.Row;
            var expected = renderer.RunGameFilter(game.Bytecode, unfiltered, filter);
            var actual = renderer.Run(OurShader.Value, pixels, inputs, ShaderParams.From(grade));

            for (var i = 0; i < pixels.Length; i++)
                for (var c = 0; c < 4; c++)
                    Assert.True(MathF.Abs(expected[i][c] - actual[i][c]) <= 1e-5f,
                        $"{filter.EnglishName}, pixel {i}, channel {c}: game {expected[i][c]}, FlashColor {actual[i][c]}");
        }
    }

    [Fact]
    public void HalfStrengthFilterIsHalfway()
    {
        var none = Run(new GradeSettings());
        var full = Run(new GradeSettings { GameFilter = 3 });
        var half = Run(new GradeSettings { GameFilter = 3, GameFilterStrength = 0.5f });

        for (var i = 0; i < Pixels.Length; i++)
            for (var c = 0; c < 3; c++)
                Assert.Equal((Clamp01(none[i][c]) + full[i][c]) / 2f, half[i][c], 5);
        Assert.True(MathF.Abs(full[15][2] - Clamp01(none[15][2])) > 0.05f, "Sepia should change the blue sky.");
    }

    [Fact]
    public void MonochromeFilterGivesGreys()
    {
        foreach (var pixel in Run(new GradeSettings { GameFilter = 4 }))
        {
            Assert.Equal(pixel[0], pixel[1], 5);
            Assert.Equal(pixel[0], pixel[2], 5);
        }
    }

    [Fact]
    public void ShaderTakesTheSameInputsAsTheGame()
    {
        var game = GameFiles.ToneMapping;
        if (game == null)
            return;

        Assert.Equal(Section(Disassemble(game.Bytecode), "// Input signature:"), Section(Disassemble(OurShader.Value), "// Input signature:"));
        Assert.Equal(Section(Disassemble(game.Bytecode), "// Output signature:"), Section(Disassemble(OurShader.Value), "// Output signature:"));
        Assert.Equal(
            ["cb0", "cb1", "cb13", "s0", "s1", "s2", "t0", "t1", "t2"],
            Bindings(Disassemble(OurShader.Value)));
    }

    private float[][] Run(GradeSettings settings) => renderer.Run(OurShader.Value, Pixels, inputs, ShaderParams.From(settings));

    private static float[] Pixel(float grey) => [grey, grey, grey, 0.5f];

    private static float[] Pixel(float r, float g, float b) => [r, g, b, 0.5f];

    // La sortie est en « gamma 2 » : la lumière est son carré.
    private static float[] Linear(float[] pixel) => [pixel[0] * pixel[0], pixel[1] * pixel[1], pixel[2] * pixel[2]];

    // Écart entre la couleur la plus forte et la plus faible, rapporté à la plus forte (0 : gris).
    private static float Colorfulness(float[] color) => (color.Max() - color.Min()) / color.Max();

    private static unsafe string Disassemble(byte[] code)
    {
        ID3DBlob* text;
        fixed (byte* p = code)
            Assert.True(DirectX.D3DDisassemble(p, (nuint)code.Length, 0, null, &text) >= 0);
        try
        {
            return Encoding.ASCII.GetString((byte*)text->GetBufferPointer(), (int)text->GetBufferSize()).TrimEnd('\0');
        }
        finally
        {
            text->Release();
        }
    }

    // Les lignes du tableau qui suit le titre, jusqu'à la ligne vide de commentaire.
    private static string[] Section(string disassembly, string title) => disassembly.Split('\n')
        .Select(l => l.TrimEnd())
        .SkipWhile(l => l != title)
        .Skip(1)
        .SkipWhile(l => l == "//")
        .TakeWhile(l => l != "//")
        .ToArray();

    private static string[] Bindings(string disassembly) => Section(disassembly, "// Resource Bindings:")
        .Skip(2)
        .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^2])
        .Order(StringComparer.Ordinal)
        .ToArray();

    // Ce que fait la passe suivante du jeu (Saturate.shcd).
    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
