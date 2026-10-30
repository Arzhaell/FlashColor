using System.Numerics;
using System.Runtime.InteropServices;

namespace FlashColor.Tests;

public class ShaderParamsTests
{
    private static readonly Vector3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

    [Fact]
    public void LayoutMatchesTheShaderBuffer()
    {
        Assert.Equal(ShaderParams.Size, Marshal.SizeOf<ShaderParams>());
        Assert.Equal(0, ShaderParams.Size % 16);
    }

    [Fact]
    public void DefaultSettingsOnlySoftenBrightLights()
    {
        var parameters = ShaderParams.From(new GradeSettings());

        Assert.Equal(Vector3.One, parameters.Gain);
        Assert.Equal(1f, parameters.Contrast);
        Assert.Equal(1f, parameters.Saturation);
        Assert.Equal(0f, parameters.Vibrance);
        Assert.Equal(0.8f * 0.8f, parameters.ShoulderStart, 6);
        // Zones, couleur par couleur et détails sautés : le shader fait le même calcul que le jeu.
        Assert.Equal(Vector4.Zero, parameters.Flags);
        Assert.Equal(new Vector4(1f, 1f, 1f, 0f), parameters.ShadowTint);
        Assert.Equal(Vector4.One, parameters.HueSaturationA);
        Assert.Equal(Vector4.One, parameters.HueSaturationB);
    }

    [Fact]
    public void EachBlockTurnsItsStepOn()
    {
        Assert.Equal(1f, ShaderParams.From(new GradeSettings { Midtones = new ToneZone { Brightness = 0.2f } }).Flags.X);
        Assert.Equal(1f, ShaderParams.From(new GradeSettings { Shadows = new ToneZone { Hue = 200f, Strength = 0.1f } }).Flags.X);
        // Une couleur choisie sans force ne colore rien : l'étape reste sautée.
        Assert.Equal(0f, ShaderParams.From(new GradeSettings { Shadows = new ToneZone { Hue = 200f } }).Flags.X);

        var perColor = new GradeSettings();
        perColor.Colors[6].Saturation = 1.5f;
        Assert.Equal(1f, ShaderParams.From(perColor).Flags.Y);

        Assert.Equal(1f, ShaderParams.From(new GradeSettings { Sharpness = 0.3f }).Flags.Z);
        Assert.Equal(1f, ShaderParams.From(new GradeSettings { Clarity = -0.3f }).Flags.Z);
    }

    [Fact]
    public void GPoseFilterGoesToItsSlots()
    {
        // Monochrome 1 : chaque couleur devient la luminosité.
        var parameters = ShaderParams.From(new GradeSettings { GameFilter = 4, GameFilterStrength = 0.7f });

        Assert.Equal(new Vector4(0.29891f, 0.58661f, 0.11448f, 0f), parameters.FilterRed);
        Assert.Equal(parameters.FilterRed, parameters.FilterGreen);
        Assert.Equal(parameters.FilterRed, parameters.FilterBlue);
        Assert.Equal(new Vector4(0.7f, 4f, 0f, 0f), parameters.Filter);
    }

    [Fact]
    public void NoGPoseFilterSkipsTheStep()
    {
        Assert.Equal(Vector4.Zero, ShaderParams.From(new GradeSettings()).Filter);
        Assert.Equal(Vector4.Zero, ShaderParams.From(new GradeSettings { GameFilter = 4, GameFilterStrength = 0f }).Filter);
        // Ligne inconnue (« Standard », ou un réglage d'une autre version) : pas de filtre.
        Assert.Equal(Vector4.Zero, ShaderParams.From(new GradeSettings { GameFilter = 9999 }).Filter);
        Assert.Equal(1f, ShaderParams.From(new GradeSettings { GameFilter = 4, GameFilterStrength = 3f }).Filter.X);
    }

    [Fact]
    public void GPoseFilterTableIsComplete()
    {
        Assert.Equal(24, GameFilters.All.Count);
        Assert.Equal(GameFilters.All.Count, GameFilters.All.Select(f => f.Row).Distinct().Count());
        Assert.Null(GameFilters.Find(0));
        foreach (var filter in GameFilters.All)
        {
            Assert.NotEqual(0u, filter.Row);
            Assert.Same(filter, GameFilters.Find(filter.Row));
            Assert.Equal(12, filter.Matrix.Length);
            Assert.Equal(256, filter.Curve.Length);
            Assert.False(string.IsNullOrWhiteSpace(filter.EnglishName) || string.IsNullOrWhiteSpace(filter.FrenchName));
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(200f)]
    [InlineData(260f)]
    public void ZoneColorKeepsTheBrightness(float hue)
    {
        var tint = ShaderParams.From(new GradeSettings { Highlights = new ToneZone { Hue = hue, Strength = 1f } }).HighlightTint;

        Assert.Equal(1f, Vector3.Dot(new Vector3(tint.X, tint.Y, tint.Z), LumaWeights), 5);
    }

    [Fact]
    public void ColorsGoToTheirSlots()
    {
        var settings = new GradeSettings();
        settings.Colors[0].Hue = 30f;        // rouge
        settings.Colors[3].Saturation = 0f;  // vert
        settings.Colors[7].Luminance = -1f;  // magenta
        var parameters = ShaderParams.From(settings);

        Assert.Equal(30f / 360f, parameters.HueShiftA.X, 6);
        Assert.Equal(0f, parameters.HueSaturationA.W);
        Assert.Equal(-1f, parameters.HueLuminanceB.W);
    }

    [Fact]
    public void MissingColorsAreNeutral()
    {
        var parameters = ShaderParams.From(new GradeSettings { Colors = [new ColorAdjust { Saturation = 0.5f }] });

        Assert.Equal(0.5f, parameters.HueSaturationA.X);
        Assert.Equal(1f, parameters.HueSaturationB.W);
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(60f, 1f, 1f, 0f)]
    [InlineData(120f, 0f, 1f, 0f)]
    [InlineData(240f, 0f, 0f, 1f)]
    [InlineData(300f, 1f, 0f, 1f)]
    public void HueColorsArePure(float hue, float r, float g, float b)
    {
        var color = ShaderParams.HueColor(hue);

        Assert.Equal(r, color.X, 5);
        Assert.Equal(g, color.Y, 5);
        Assert.Equal(b, color.Z, 5);
    }

    [Fact]
    public void RolloffOffDisablesTheShoulder()
    {
        var parameters = ShaderParams.From(new GradeSettings { HighlightRolloff = false });

        Assert.Equal(1f, parameters.ShoulderStart);
    }

    [Fact]
    public void ExposureDoublesTheLightPerStop()
    {
        var parameters = ShaderParams.From(new GradeSettings { Exposure = 1f });

        Assert.Equal(new Vector3(2f), parameters.Gain);
    }

    [Theory]
    [InlineData(100f, 0f)]
    [InlineData(-100f, 0f)]
    [InlineData(0f, 100f)]
    [InlineData(-60f, -40f)]
    public void WhiteBalanceKeepsTheBrightness(float temperature, float tint)
    {
        var gain = ShaderParams.From(new GradeSettings { Temperature = temperature, Tint = tint }).Gain;

        Assert.Equal(1f, Vector3.Dot(gain, LumaWeights), 5);
    }

    [Fact]
    public void WarmerMeansMoreRedLessBlue()
    {
        var gain = ShaderParams.From(new GradeSettings { Temperature = 50f }).Gain;

        Assert.True(gain.X > gain.Y && gain.Y > gain.Z, $"Gain {gain}");
    }

    [Fact]
    public void MagentaMeansLessGreen()
    {
        var gain = ShaderParams.From(new GradeSettings { Tint = 50f }).Gain;

        Assert.True(gain.Y < gain.X && gain.Y < gain.Z, $"Gain {gain}");
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var parameters = ShaderParams.From(new GradeSettings { Contrast = 10f, Saturation = -1f, RolloffStart = 2f, KeepHighlightColor = 5f, KeepDetails = 3f });

        Assert.Equal(2f, parameters.Contrast);
        Assert.Equal(0f, parameters.Saturation);
        Assert.Equal(0.99f * 0.99f, parameters.ShoulderStart, 6);
        Assert.Equal(1f, parameters.KeepColor);
        Assert.Equal(1f, parameters.Local.X);
    }
}
