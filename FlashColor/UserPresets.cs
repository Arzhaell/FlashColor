using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashColor;

/// <summary>Enregistrer, renommer et supprimer les préréglages du joueur (les noms sont uniques, à la casse près).</summary>
internal static class UserPresets
{
    public static UserPreset? Find(List<UserPreset> presets, string name) =>
        presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Le préréglage du joueur dont les réglages sont ceux-ci, ou null.</summary>
    public static UserPreset? Matching(List<UserPreset> presets, GradeSettings settings) =>
        presets.FirstOrDefault(p => p.Settings.SameAs(settings));

    /// <summary>Enregistre une copie des réglages sous ce nom ; un préréglage du même nom est remplacé.</summary>
    public static void Save(List<UserPreset> presets, string name, GradeSettings settings)
    {
        name = name.Trim();
        var existing = Find(presets, name);
        if (existing != null)
            existing.Settings = settings.Clone();
        else
            presets.Add(new UserPreset { Name = name, Settings = settings.Clone() });
    }

    /// <summary>Faux si le nouveau nom est vide ou déjà pris par un autre préréglage.</summary>
    public static bool Rename(List<UserPreset> presets, string oldName, string newName)
    {
        newName = newName.Trim();
        var preset = Find(presets, oldName);
        var other = Find(presets, newName);
        if (preset == null || newName.Length == 0 || (other != null && other != preset))
            return false;

        preset.Name = newName;
        return true;
    }

    public static void Delete(List<UserPreset> presets, string name)
    {
        var preset = Find(presets, name);
        if (preset != null)
            presets.Remove(preset);
    }
}
