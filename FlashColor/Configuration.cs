using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace FlashColor;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public PluginLanguage Language { get; set; } = PluginLanguage.Auto;

    /// <summary>Remplacer le tonemap du jeu ; décoché, l'image est celle du jeu.</summary>
    public bool Enabled { get; set; } = true;

    public GradeSettings Grade { get; set; } = new();

    /// <summary>Préréglages enregistrés par le joueur, dans l'ordre du menu.</summary>
    // Replace : sinon la lecture du fichier ajouterait les préréglages lus à la liste de départ.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<UserPreset> UserPresets { get; set; } = [];

    /// <summary>Force du flash blanc, en crans d'exposition (IL) au plus fort du flash.</summary>
    public float FlashStrength { get; set; } = 5f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

/// <summary>Un préréglage enregistré par le joueur.</summary>
[Serializable]
public class UserPreset
{
    public string Name { get; set; } = string.Empty;

    public GradeSettings Settings { get; set; } = new();
}

/// <summary>Réglages de l'image. Les valeurs par défaut ne changent rien, sauf l'adoucissement des lumières fortes.</summary>
[Serializable]
public class GradeSettings
{
    /// <summary>Rouge, orange, jaune, vert, cyan, bleu, violet, magenta : les couleurs réglables une par une.</summary>
    public const int ColorCount = 8;

    /// <summary>En crans d'exposition (IL) : +1 double la lumière.</summary>
    public float Exposure { get; set; }

    /// <summary>1 : inchangé ; au-dessus, ombres plus sombres et lumières plus claires.</summary>
    public float Contrast { get; set; } = 1f;

    /// <summary>1 : inchangé ; 0 : noir et blanc.</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>De -1 à 1 : renforce (ou ternit) surtout les couleurs ternes, sans brûler les couleurs déjà vives.</summary>
    public float Vibrance { get; set; }

    /// <summary>De -100 (plus froid, bleu) à 100 (plus chaud, orange).</summary>
    public float Temperature { get; set; }

    /// <summary>De -100 (plus vert) à 100 (plus magenta).</summary>
    public float Tint { get; set; }

    public ToneZone Shadows { get; set; } = new();

    public ToneZone Midtones { get; set; } = new();

    public ToneZone Highlights { get; set; } = new();

    /// <summary>Réglage de chaque couleur (<see cref="ColorCount"/>), dans l'ordre rouge → magenta.</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ColorAdjust[] Colors { get; set; } = NewColors();

    /// <summary>De 0 à 1 : netteté des petits détails.</summary>
    public float Sharpness { get; set; }

    /// <summary>De -1 à 1 : contraste local (relief des formes) ; négatif adoucit.</summary>
    public float Clarity { get; set; }

    /// <summary>Filtre de la GPose appliqué partout (ligne de la feuille ColorFilter du jeu, voir <see cref="GameFilters"/>) ; 0 : aucun.</summary>
    public uint GameFilter { get; set; }

    /// <summary>De 0 à 1 : force du filtre de la GPose (1 : comme dans le jeu).</summary>
    public float GameFilterStrength { get; set; } = 1f;

    /// <summary>Adoucir les lumières fortes au lieu de les couper.</summary>
    public bool HighlightRolloff { get; set; } = true;

    /// <summary>Luminosité affichée (0 à 1) où commence l'adoucissement.</summary>
    public float RolloffStart { get; set; } = 0.8f;

    /// <summary>
    /// De 0 à 1 : 0, les lumières fortes blanchissent naturellement, couleur par couleur (une flamme orange passe au
    /// jaune puis au blanc) ; 1, elles gardent toute leur couleur.
    /// </summary>
    public float KeepHighlightColor { get; set; } = 0.2f;

    /// <summary>
    /// De 0 à 1 : part de la texture gardée quand les réglages et l'adoucissement changent la lumière (1 : toute).
    /// Ils s'appliquent à la lumière moyenne autour de chaque pixel, et l'écart du pixel à cette moyenne est remis après.
    /// </summary>
    public float KeepDetails { get; set; } = 1f;

    public static ColorAdjust[] NewColors() => Enumerable.Range(0, ColorCount).Select(_ => new ColorAdjust()).ToArray();

    /// <summary>Le réglage d'une couleur ; neutre s'il manque (fichier d'une autre version).</summary>
    public ColorAdjust Color(int index) => index < Colors.Length && Colors[index] != null ? Colors[index] : new ColorAdjust();

    public GradeSettings Clone() => JsonConvert.DeserializeObject<GradeSettings>(JsonConvert.SerializeObject(this))!;

    /// <summary>Mêmes réglages, à un centième près (les curseurs passent par des pourcentages et des degrés).</summary>
    public bool SameAs(GradeSettings other) =>
        HighlightRolloff == other.HighlightRolloff && Values().Zip(other.Values()).All(p => MathF.Abs(p.First - p.Second) < 0.005f);

    // Tous les réglages chiffrés, ramenés à des unités comparables (degrés en tours, -100..100 en -1..1).
    private IEnumerable<float> Values()
    {
        yield return Exposure;
        yield return Contrast;
        yield return Saturation;
        yield return Vibrance;
        yield return Temperature / 100f;
        yield return Tint / 100f;
        foreach (var zone in new[] { Shadows, Midtones, Highlights })
        {
            yield return zone.Brightness;
            yield return zone.Hue / 360f;
            yield return zone.Strength;
        }

        for (var i = 0; i < ColorCount; i++)
        {
            var color = Color(i);
            yield return color.Hue / 360f;
            yield return color.Saturation;
            yield return color.Luminance;
        }

        yield return Sharpness;
        yield return Clarity;
        yield return GameFilter;
        yield return GameFilterStrength;
        yield return RolloffStart;
        yield return KeepHighlightColor;
        yield return KeepDetails;
    }
}

/// <summary>Une zone de luminosité (ombres, tons moyens, lumières) : sa luminosité et la couleur qu'on lui donne.</summary>
[Serializable]
public class ToneZone
{
    /// <summary>En crans d'exposition (IL), de -1 à 1.</summary>
    public float Brightness { get; set; }

    /// <summary>Couleur donnée à la zone, en degrés (0 rouge, 120 vert, 240 bleu).</summary>
    public float Hue { get; set; }

    /// <summary>De 0 (aucune couleur) à 1.</summary>
    public float Strength { get; set; }
}

/// <summary>Réglage d'une couleur de l'image.</summary>
[Serializable]
public class ColorAdjust
{
    /// <summary>Décalage de teinte, en degrés, de -30 à 30.</summary>
    public float Hue { get; set; }

    /// <summary>1 : inchangé ; 0 : cette couleur devient grise.</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>En crans d'exposition (IL), de -1 à 1.</summary>
    public float Luminance { get; set; }
}
