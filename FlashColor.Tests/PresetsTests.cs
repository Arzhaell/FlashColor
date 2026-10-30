using System.Numerics;
using System.Linq;

namespace FlashColor.Tests;

public class PresetsTests
{
    [Fact]
    public void GameImageIsTheDefault()
    {
        Assert.Same(Presets.Game, Presets.Find(new GradeSettings()));
    }

    [Fact]
    public void EveryPresetIsRecognized()
    {
        foreach (var preset in Presets.All)
            Assert.Same(preset, Presets.Find(preset.Create()));
    }

    [Fact]
    public void MovedSliderMeansCustom()
    {
        var settings = Presets.AliveStyle.Create();
        settings.Contrast += 0.05f;

        Assert.Null(Presets.Find(settings));
    }

    [Fact]
    public void SliderRoundingKeepsThePreset()
    {
        // Le curseur affiche des pourcentages : 115 % relu donne 1.15 à un arrondi près.
        var settings = Presets.AliveStyle.Create();
        settings.Contrast = settings.Contrast * 100f / 100f + 1e-6f;

        Assert.Same(Presets.AliveStyle, Presets.Find(settings));
    }

    [Fact]
    public void PresetValuesStayInTheSliderRanges()
    {
        foreach (var preset in Presets.All)
        {
            var s = preset.Create();
            Assert.InRange(s.Exposure, -2f, 2f);
            Assert.InRange(s.Contrast, 0.5f, 2f);
            Assert.InRange(s.Saturation, 0f, 2f);
            Assert.InRange(s.Temperature, -100f, 100f);
            Assert.InRange(s.Tint, -100f, 100f);
            Assert.InRange(s.RolloffStart, 0.5f, 0.99f);
            Assert.InRange(s.KeepHighlightColor, 0f, 1f);
            Assert.InRange(s.KeepDetails, 0f, 1f);
        }
    }

    [Fact]
    public void AliveStyleHasVioletShadowsBlueMidtonesAndNeutralLights()
    {
        // Ce qu'ont donné ses captures avant/après (et, à part, ses pixels gris : bleu ×1,3 à 1,7 dans les ombres).
        var parameters = ShaderParams.From(Presets.AliveStyle.Create());

        Assert.True(parameters.ShadowTint.Z > parameters.ShadowTint.X && parameters.ShadowTint.X > parameters.ShadowTint.Y, $"Shadows {parameters.ShadowTint}");
        Assert.True(parameters.MidtoneTint.Z > 1.1f && parameters.MidtoneTint.X < 1f, $"Midtones {parameters.MidtoneTint}");
        Assert.InRange(parameters.HighlightTint.Z, 0.95f, 1.08f);
        Assert.Equal(1f, Vector3.Dot(new Vector3(parameters.ShadowTint.X, parameters.ShadowTint.Y, parameters.ShadowTint.Z), new Vector3(0.2126f, 0.7152f, 0.0722f)), 4);
    }

    [Fact]
    public void MeasuredPresetsStayCloseToTheirSource()
    {
        // Stamp's se dit léger : il doit rester tout près de l'image du jeu.
        var stamp = Presets.StampStyle.Create();
        Assert.InRange(stamp.Contrast, 0.95f, 1.05f);
        Assert.InRange(stamp.Saturation, 0.95f, 1.05f);

        // Colorful Fantasy se dit « des couleurs qui ressortent ».
        Assert.True(Presets.ColorfulStyle.Create().Vibrance >= 0.5f);
    }

    [Fact]
    public void PresetsAreGroupedInOrder()
    {
        Assert.Equal(PresetGroup.Game, Presets.All[0].Group);
        var groups = Presets.All.Select(p => p.Group).ToArray();
        Assert.Equal(groups.Order(), groups);
    }

    [Fact]
    public void UserPresetsAreSavedRenamedAndDeleted()
    {
        var presets = new List<UserPreset>();
        UserPresets.Save(presets, " Soir ", new GradeSettings { Contrast = 1.2f });
        UserPresets.Save(presets, "soir", new GradeSettings { Contrast = 1.3f });

        Assert.Single(presets);
        Assert.Equal("Soir", presets[0].Name);
        Assert.Equal(1.3f, presets[0].Settings.Contrast);

        UserPresets.Save(presets, "Jour", new GradeSettings());
        Assert.False(UserPresets.Rename(presets, "Soir", "JOUR"));
        Assert.True(UserPresets.Rename(presets, "Soir", "Nuit"));
        Assert.NotNull(UserPresets.Find(presets, "nuit"));

        UserPresets.Delete(presets, "Nuit");
        Assert.Equal(["Jour"], presets.Select(p => p.Name));
    }

    [Fact]
    public void SavedPresetIsACopy()
    {
        var presets = new List<UserPreset>();
        var settings = new GradeSettings();
        UserPresets.Save(presets, "Copie", settings);
        settings.Shadows.Strength = 1f;
        settings.Colors[3].Saturation = 0f;

        Assert.Equal(0f, presets[0].Settings.Shadows.Strength);
        Assert.Equal(1f, presets[0].Settings.Colors[3].Saturation);
    }

    [Fact]
    public void UserPresetIsRecognizedByItsSettings()
    {
        var presets = new List<UserPreset>();
        UserPresets.Save(presets, "Mien", new GradeSettings { Vibrance = 0.3f, Midtones = new ToneZone { Hue = 40f, Strength = 0.2f } });

        Assert.Equal("Mien", UserPresets.Matching(presets, new GradeSettings { Vibrance = 0.3f, Midtones = new ToneZone { Hue = 40.001f, Strength = 0.2f } })?.Name);
        Assert.Null(UserPresets.Matching(presets, new GradeSettings { Vibrance = 0.3f }));
    }

    [Fact]
    public void SettingsSurviveCopyAndPaste()
    {
        var settings = Presets.AliveStyle.Create();
        settings.Colors[5].Luminance = -0.4f;
        settings.Sharpness = 0.5f;
        settings.GameFilter = 4;
        settings.GameFilterStrength = 0.6f;

        var pasted = SettingsText.Read(SettingsText.Write(settings));

        Assert.NotNull(pasted);
        Assert.True(pasted.SameAs(settings));
        Assert.Null(SettingsText.Read("bonjour"));
        Assert.Null(SettingsText.Read("FlashColor:{pas du json"));
        Assert.Null(SettingsText.Read(null));
    }
}
