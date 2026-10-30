using System;
using Newtonsoft.Json;

namespace FlashColor;

/// <summary>Réglages en texte, pour les copier, les coller et les partager.</summary>
internal static class SettingsText
{
    private const string Prefix = "FlashColor:";

    public static string Write(GradeSettings settings) => Prefix + JsonConvert.SerializeObject(settings, Formatting.None);

    /// <summary>Les réglages contenus dans ce texte, ou null si ce n'en est pas.</summary>
    public static GradeSettings? Read(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        try
        {
            // Sans TypeNameHandling (valeur par défaut) : un texte collé ne peut créer que des réglages.
            return JsonConvert.DeserializeObject<GradeSettings>(text[Prefix.Length..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
