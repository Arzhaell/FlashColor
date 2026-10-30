using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FlashColor;

/// <summary>Réglages tels que le shader les lit (registre b13), dans l'ordre de <c>FlashColorParams</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal record struct ShaderParams
{
    public Vector3 Gain;
    public float Contrast;
    public float Saturation;
    public float ShoulderStart;

    /// <summary>0 : les lumières fortes blanchissent couleur par couleur ; 1 : gardent leur couleur.</summary>
    public float KeepColor;

    public float Vibrance;

    /// <summary>Par zone : gains RGB (même luminosité) et, dans w, la luminosité en IL.</summary>
    public Vector4 ShadowTint;
    public Vector4 MidtoneTint;
    public Vector4 HighlightTint;

    /// <summary>x : netteté, y : clarté, z : rayon de la clarté (part de la hauteur de l'image).</summary>
    public Vector4 Detail;

    /// <summary>x : zones, y : couleur par couleur, z : détails ; 0 : étape sautée (calcul identique au jeu).</summary>
    public Vector4 Flags;

    /// <summary>Par couleur, de rouge (A.x) à magenta (B.w) : décalage de teinte en tours, saturation, luminosité en IL.</summary>
    public Vector4 HueShiftA, HueShiftB;
    public Vector4 HueSaturationA, HueSaturationB;
    public Vector4 HueLuminanceA, HueLuminanceB;

    /// <summary>Garder les détails : x part du détail gardée, y rayon (part de la hauteur), z tolérance des bords (crans), w 1 : actif.</summary>
    public Vector4 Local;

    /// <summary>Filtre de la GPose : les trois lignes de sa matrice de couleurs (3 couleurs, puis décalage).</summary>
    public Vector4 FilterRed;
    public Vector4 FilterGreen;
    public Vector4 FilterBlue;

    /// <summary>Filtre de la GPose : x force (0 : pas de filtre), y ligne de la feuille ColorFilter du jeu.</summary>
    public Vector4 Filter;

    /// <summary>Taille du tampon de constantes : 18 registres de 16 octets.</summary>
    public const int Size = 288;

    // Garder les détails : moyenne sur deux anneaux jusqu'à 1,5 % de la hauteur de l'image (une vingtaine de pixels
    // en 1440p) ; un voisin à plus d'un cran d'écart compte de moins en moins (bords francs).
    private const float LocalRadius = 0.015f;
    private const float LocalEdgeStops = 1f;

    // Poids de la luminosité (Rec. 709), les mêmes que dans le shader.
    private static readonly Vector3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

    // Écart de couleur au bout des réglages de température et de teinte (100).
    private const float TemperatureRange = 0.25f;
    private const float TintRange = 0.2f;

    // Rayon de la clarté : 2 % de la hauteur de l'image (une vingtaine de pixels en 1080p).
    private const float ClarityRadius = 0.02f;

    public static ShaderParams From(GradeSettings settings)
    {
        var temperature = Math.Clamp(settings.Temperature, -100f, 100f) / 100f;
        var tint = Math.Clamp(settings.Tint, -100f, 100f) / 100f;

        // Plus chaud : plus de rouge, moins de bleu ; plus magenta : moins de vert. Ramené à la même luminosité,
        // pour que la balance des blancs ne rende pas l'image plus claire ou plus sombre.
        var balance = Vector3.One;
        if (temperature != 0f || tint != 0f)
        {
            balance = new Vector3(1f + TemperatureRange * temperature, 1f - TintRange * tint, 1f - TemperatureRange * temperature);
            balance /= Vector3.Dot(balance, LumaWeights);
        }

        // L'image sort du shader en « gamma 2 » : la luminosité affichée x correspond à la lumière x².
        var rolloffStart = Math.Clamp(settings.RolloffStart, 0.5f, 0.99f);

        var parameters = new ShaderParams
        {
            Gain = balance * MathF.Pow(2f, Math.Clamp(settings.Exposure, -3f, 3f)),
            Contrast = Math.Clamp(settings.Contrast, 0.5f, 2f),
            Saturation = Math.Clamp(settings.Saturation, 0f, 2f),
            ShoulderStart = settings.HighlightRolloff ? rolloffStart * rolloffStart : 1f,
            KeepColor = Math.Clamp(settings.KeepHighlightColor, 0f, 1f),
            Vibrance = Math.Clamp(settings.Vibrance, -1f, 1f),
            ShadowTint = Zone(settings.Shadows),
            MidtoneTint = Zone(settings.Midtones),
            HighlightTint = Zone(settings.Highlights),
            Detail = new Vector4(Math.Clamp(settings.Sharpness, 0f, 1f), Math.Clamp(settings.Clarity, -1f, 1f), ClarityRadius, 0f),
        };

        var shifts = new float[GradeSettings.ColorCount];
        var saturations = new float[GradeSettings.ColorCount];
        var luminances = new float[GradeSettings.ColorCount];
        var perColor = false;
        for (var i = 0; i < GradeSettings.ColorCount; i++)
        {
            var color = settings.Color(i);
            shifts[i] = Math.Clamp(color.Hue, -30f, 30f) / 360f;
            saturations[i] = Math.Clamp(color.Saturation, 0f, 2f);
            luminances[i] = Math.Clamp(color.Luminance, -1f, 1f);
            perColor |= shifts[i] != 0f || saturations[i] != 1f || luminances[i] != 0f;
        }

        parameters.HueShiftA = new Vector4(shifts[0], shifts[1], shifts[2], shifts[3]);
        parameters.HueShiftB = new Vector4(shifts[4], shifts[5], shifts[6], shifts[7]);
        parameters.HueSaturationA = new Vector4(saturations[0], saturations[1], saturations[2], saturations[3]);
        parameters.HueSaturationB = new Vector4(saturations[4], saturations[5], saturations[6], saturations[7]);
        parameters.HueLuminanceA = new Vector4(luminances[0], luminances[1], luminances[2], luminances[3]);
        parameters.HueLuminanceB = new Vector4(luminances[4], luminances[5], luminances[6], luminances[7]);

        var zones = parameters.ShadowTint != Neutral || parameters.MidtoneTint != Neutral || parameters.HighlightTint != Neutral;
        var detail = parameters.Detail.X != 0f || parameters.Detail.Y != 0f;
        parameters.Flags = new Vector4(zones ? 1f : 0f, perColor ? 1f : 0f, detail ? 1f : 0f, 0f);

        // Garder les détails n'a de sens que si quelque chose change la lumière : sinon l'étape est sautée et l'image
        // reste exactement celle du jeu.
        var keepDetails = Math.Clamp(settings.KeepDetails, 0f, 1f);
        var changesLight = settings.HighlightRolloff || parameters.Gain != Vector3.One || parameters.Contrast != 1f
            || parameters.Saturation != 1f || parameters.Vibrance != 0f || zones || perColor;
        parameters.Local = new Vector4(keepDetails, LocalRadius, LocalEdgeStops, keepDetails > 0f && changesLight ? 1f : 0f);

        var filter = GameFilters.Find(settings.GameFilter);
        var filterStrength = Math.Clamp(settings.GameFilterStrength, 0f, 1f);
        if (filter != null && filterStrength > 0f)
        {
            var m = filter.Matrix;
            parameters.FilterRed = new Vector4(m[0], m[1], m[2], m[3]);
            parameters.FilterGreen = new Vector4(m[4], m[5], m[6], m[7]);
            parameters.FilterBlue = new Vector4(m[8], m[9], m[10], m[11]);
            parameters.Filter = new Vector4(filterStrength, filter.Row, 0f, 0f);
        }

        return parameters;
    }

    /// <summary>Couleur pure d'une teinte (en degrés), comme dans le shader.</summary>
    public static Vector3 HueColor(float degrees)
    {
        var h = degrees / 360f;
        static float Channel(float x) => Math.Clamp(MathF.Abs((x - MathF.Floor(x)) * 6f - 3f) - 1f, 0f, 1f);
        return new Vector3(Channel(h + 1f), Channel(h + 2f / 3f), Channel(h + 1f / 3f));
    }

    private static readonly Vector4 Neutral = new(1f, 1f, 1f, 0f);

    // Gains d'une zone : on s'écarte du gris vers la couleur choisie, sans changer la luminosité (même somme pondérée).
    private static Vector4 Zone(ToneZone zone)
    {
        var strength = Math.Clamp(zone.Strength, 0f, 1f);
        var color = HueColor(zone.Hue);
        var gain = Vector3.One + strength * (color - new Vector3(Vector3.Dot(color, LumaWeights)));
        return new Vector4(gain, Math.Clamp(zone.Brightness, -1f, 1f));
    }
}
