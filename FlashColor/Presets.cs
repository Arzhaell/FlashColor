using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashColor;

internal enum PresetGroup
{
    Game,
    /// <summary>Couleurs de presets ReShade connus, mesurées sur leurs captures avant/après.</summary>
    Measured,
    /// <summary>Styles créés pour FlashColor.</summary>
    Ambiance,
    /// <summary>Les filtres de la GPose du jeu, à l'identique.</summary>
    GPose,
}

/// <summary>Un préréglage proposé dans la fenêtre : un nom, une explication, des réglages.</summary>
internal sealed record Preset(PresetGroup Group, string EnglishName, string FrenchName, string EnglishHint, string FrenchHint, Func<GradeSettings> Create)
{
    public string Name => Loc.T(EnglishName, FrenchName);

    public string Hint => Loc.T(EnglishHint, FrenchHint);
}

internal static class Presets
{
    public static readonly Preset Game = new(
        PresetGroup.Game, "Game image", "Image du jeu",
        "The game's own colors; only bright lights are softened.",
        "Les couleurs du jeu ; seules les lumières fortes sont adoucies.",
        () => new GradeSettings());

    // Les préréglages « Façon … » sont mesurés sur les captures avant/après publiées par leurs auteurs (2026-10-14) :
    // le calcul de FlashColor (exposition, contraste, saturation, vibrance, couleur et luminosité des trois zones)
    // a été ajusté pour transformer au mieux chaque image « avant » en image « après » (écart des logarithmes, sur
    // 2 500 pixels par paire), avec une petite pénalité sur les réglages extrêmes pour garder l'explication la plus
    // simple. Paires mal alignées (corrélation < 0,93) écartées. Rien n'est repris de leurs fichiers. Leur ombrage
    // d'ambiance, leur netteté, leur brouillard ou leur flou ne sont pas des couleurs : pas reproduits.

    /// <summary>Alive (Wafflezs, mod 156), le plus suivi : 2 paires, écart 0,60 → 0,35.</summary>
    public static readonly Preset AliveStyle = new(
        PresetGroup.Measured, "Alive style (ReShade)", "Façon Alive (ReShade)",
        "The colors of Alive, the most popular FFXIV ReShade preset, measured on its before/after screenshots: livelier dull colors, violet shadows, blue midtones, nearly neutral lights.",
        "Les couleurs d'Alive, le preset ReShade le plus populaire de FF14, mesurées sur ses captures avant/après : couleurs ternes ravivées, ombres violettes, tons moyens bleutés, lumières presque neutres.",
        () => new GradeSettings
        {
            Contrast = 1.05f,
            Saturation = 1.07f,
            Vibrance = 0.4f,
            Shadows = new ToneZone { Brightness = -0.14f, Hue = 276f, Strength = 0.4f },
            Midtones = new ToneZone { Brightness = 0.06f, Hue = 232f, Strength = 0.3f },
            Highlights = new ToneZone { Brightness = 0.13f, Hue = 266f, Strength = 0.06f },
        });

    /// <summary>Arfien (mod 684) : 4 paires ON/OFF de la v1.4, écart 1,64 → 1,08 (le moins fiable : scènes très variables).</summary>
    public static readonly Preset ArfienStyle = new(
        PresetGroup.Measured, "Arfien style (ReShade)", "Façon Arfien (ReShade)",
        "The colors of Arfien's preset, measured on its ON/OFF screenshots: more contrast, much more saturation, warm pinkish shadows. Its shading and sharpening are not reproduced.",
        "Les couleurs du preset d'Arfien, mesurées sur ses captures ON/OFF : plus de contraste, beaucoup plus de saturation, ombres rose chaud. Son ombrage et sa netteté ne sont pas reproduits.",
        () => new GradeSettings
        {
            Contrast = 1.13f,
            Saturation = 1.44f,
            Vibrance = -0.17f,
            Shadows = new ToneZone { Brightness = -0.05f, Hue = 336f, Strength = 0.66f },
            Midtones = new ToneZone { Brightness = 0.29f, Hue = 256f, Strength = 0.06f },
            Highlights = new ToneZone { Brightness = -0.03f, Hue = 195f, Strength = 0.17f },
        });

    /// <summary>Stamp's Enhanced Gameplay (mod 1651) : 4 paires Off/On de la v4.0, écart 0,42 → 0,17.</summary>
    public static readonly Preset StampStyle = new(
        PresetGroup.Measured, "Stamp's style (ReShade)", "Façon Stamp's (ReShade)",
        "The colors of Stamp's Enhanced Gameplay, a light preset for long sessions, measured on its Off/On screenshots: very close to the game, slightly brighter lights.",
        "Les couleurs de Stamp's Enhanced Gameplay, un preset léger pour les longues sessions, mesurées sur ses captures Off/On : très proche du jeu, lumières un peu plus claires.",
        () => new GradeSettings
        {
            Exposure = -0.15f,
            Contrast = 1.03f,
            Saturation = 1.02f,
            Vibrance = 0.05f,
            Highlights = new ToneZone { Brightness = 0.15f, Hue = 298f, Strength = 0.06f },
            Midtones = new ToneZone { Brightness = -0.04f, Hue = 275f, Strength = 0.08f },
        });

    /// <summary>Colorful Fantasy (mod 790) : 2 paires Off/On (2 autres mal alignées), écart 1,47 → 0,85.</summary>
    public static readonly Preset ColorfulStyle = new(
        PresetGroup.Measured, "Colorful Fantasy style (ReShade)", "Façon Colorful Fantasy (ReShade)",
        "The colors of Colorful Fantasy, measured on its Off/On screenshots: colors that pop (strong vibrance), more contrast, magenta tones.",
        "Les couleurs de Colorful Fantasy, mesurées sur ses captures Off/On : des couleurs qui ressortent (forte vibrance), plus de contraste, tons magenta.",
        () => new GradeSettings
        {
            Exposure = 0.1f,
            Contrast = 1.17f,
            Saturation = 1.22f,
            Vibrance = 1f,
            Shadows = new ToneZone { Brightness = 0.03f, Hue = 313f, Strength = 0.27f },
            Midtones = new ToneZone { Brightness = -0.25f, Hue = 309f, Strength = 0.27f },
            Highlights = new ToneZone { Brightness = 0.19f, Hue = 354f, Strength = 0.19f },
        });

    /// <summary>Simple and Clean (mod 1592) : 4 paires On/Off des versions 1 et 2 (la 3 n'en publie pas), écart 0,93 → 0,53.</summary>
    public static readonly Preset SimpleStyle = new(
        PresetGroup.Measured, "Simple and Clean style (ReShade)", "Façon Simple and Clean (ReShade)",
        "The colors of Simple and Clean (versions 1 and 2), measured on its On/Off screenshots: brighter, livelier, rosy tones.",
        "Les couleurs de Simple and Clean (versions 1 et 2), mesurées sur ses captures On/Off : plus lumineux, plus vivant, tons rosés.",
        () => new GradeSettings
        {
            Exposure = 0.22f,
            Contrast = 1.08f,
            Vibrance = 0.33f,
            Shadows = new ToneZone { Brightness = -0.13f, Hue = 318f, Strength = 0.38f },
            Midtones = new ToneZone { Brightness = 0.24f, Hue = 336f, Strength = 0.4f },
            Highlights = new ToneZone { Brightness = 0.15f, Hue = 337f, Strength = 0.13f },
        });

    public static readonly Preset Cinema = new(
        PresetGroup.Ambiance, "Cinema", "Cinéma",
        "Teal shadows, orange lights, more contrast.",
        "Ombres bleu-vert, lumières orangées, plus de contraste.",
        () => new GradeSettings
        {
            Contrast = 1.15f,
            Saturation = 0.95f,
            Vibrance = 0.1f,
            Shadows = new ToneZone { Brightness = -0.1f, Hue = 190f, Strength = 0.35f },
            Highlights = new ToneZone { Hue = 30f, Strength = 0.25f },
        });

    public static readonly Preset Pastel = new(
        PresetGroup.Ambiance, "Soft pastel", "Pastel doux",
        "Lighter, softer and less saturated: a dreamy look.",
        "Plus clair, plus doux et moins saturé : une image rêveuse.",
        () => new GradeSettings
        {
            Exposure = 0.15f,
            Contrast = 0.85f,
            Saturation = 0.85f,
            Vibrance = 0.2f,
            Highlights = new ToneZone { Hue = 340f, Strength = 0.1f },
            Clarity = -0.3f,
        });

    public static readonly Preset ColdNight = new(
        PresetGroup.Ambiance, "Cold night", "Nuit froide",
        "Darker and colder, blue shadows, muted colors.",
        "Plus sombre et plus froid, ombres bleues, couleurs atténuées.",
        () => new GradeSettings
        {
            Exposure = -0.3f,
            Contrast = 1.1f,
            Saturation = 0.85f,
            Temperature = -35f,
            Shadows = new ToneZone { Brightness = -0.2f, Hue = 230f, Strength = 0.4f },
        });

    public static readonly Preset Sunset = new(
        PresetGroup.Ambiance, "Sunset", "Coucher de soleil",
        "Warm golden light, orange lights and midtones.",
        "Lumière chaude et dorée, lumières et tons moyens orangés.",
        () => new GradeSettings
        {
            Saturation = 1.1f,
            Temperature = 35f,
            Tint = 10f,
            Midtones = new ToneZone { Hue = 20f, Strength = 0.2f },
            Highlights = new ToneZone { Hue = 30f, Strength = 0.35f },
        });

    public static readonly Preset BlackAndWhite = new(
        PresetGroup.Ambiance, "Contrasted black and white", "Noir et blanc contrasté",
        "Black and white with strong contrast and more relief.",
        "Noir et blanc, contraste fort et plus de relief.",
        () => new GradeSettings { Contrast = 1.35f, Saturation = 0f, Clarity = 0.3f });

    public static readonly Preset Vivid = new(
        PresetGroup.Ambiance, "Very vivid", "Très vivant",
        "Rich colors, more contrast, crisper details.",
        "Couleurs riches, plus de contraste, détails plus nets.",
        () => new GradeSettings { Contrast = 1.1f, Saturation = 1.2f, Vibrance = 0.5f, Clarity = 0.2f, Sharpness = 0.3f });

    /// <summary>Un préréglage par filtre de la GPose : l'image du jeu avec ce filtre.</summary>
    private static IEnumerable<Preset> GPoseFilters() => GameFilters.All.Select(filter => new Preset(
        PresetGroup.GPose, $"GPose: {filter.EnglishName}", $"GPose : {filter.FrenchName}",
        $"The game's « {filter.EnglishName} » GPose filter, exactly as in the game, everywhere.",
        $"Le filtre « {filter.FrenchName} » de la GPose du jeu, à l'identique, partout.",
        () => new GradeSettings { GameFilter = filter.Row }));

    public static IReadOnlyList<Preset> All { get; } =
    [
        Game,
        AliveStyle, ArfienStyle, StampStyle, ColorfulStyle, SimpleStyle,
        Cinema, Pastel, ColdNight, Sunset, BlackAndWhite, Vivid,
        .. GPoseFilters(),
    ];

    /// <summary>Le préréglage dont les réglages sont ceux-ci, ou null s'ils ont été modifiés à la main.</summary>
    public static Preset? Find(GradeSettings settings)
    {
        foreach (var preset in All)
        {
            if (preset.Create().SameAs(settings))
                return preset;
        }

        return null;
    }
}
