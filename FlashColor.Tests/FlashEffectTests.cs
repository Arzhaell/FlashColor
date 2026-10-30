namespace FlashColor.Tests;

public class FlashEffectTests
{
    [Fact]
    public void NoFlashMeansNoChange()
    {
        var flash = new FlashEffect();

        Assert.Equal(1f, flash.Gain(1000));
        Assert.Null(flash.Current(1000));
    }

    [Fact]
    public void FlashRisesHoldsThenFades()
    {
        Assert.Equal(0, FlashEffect.Level(0));
        Assert.Equal(0.5, FlashEffect.Level(30), 6);
        Assert.Equal(1, FlashEffect.Level(100));
        Assert.True(FlashEffect.Level(300) < FlashEffect.Level(200));
        Assert.Equal(0, FlashEffect.Level(2000));
    }

    [Fact]
    public void PeakMultipliesTheLightByTwoPowerStrength()
    {
        var flash = new FlashEffect();
        flash.Start(1000, 5f, gameImage: false);

        Assert.Equal(32f, flash.Gain(1100), 3);
    }

    [Fact]
    public void FlashEndsAndKeepsItsMode()
    {
        var flash = new FlashEffect();
        flash.Start(1000, 4f, gameImage: true);

        Assert.True(flash.Current(1100)!.GameImage);
        Assert.Null(flash.Current(3000));
        Assert.Equal(1f, flash.Gain(3000));
    }
}
