namespace FlashColor.Tests;

/// <summary>Lecture du vrai ToneMapping.shcd du jeu installé (lecture seule).</summary>
public class ShaderCodeFileTests
{
    [Fact]
    public void GameToneMappingIsTheSupportedVersion()
    {
        var file = GameFiles.ToneMapping;
        if (file == null)
            return;

        Assert.Equal(ToneMapReplacer.SupportedCrc, file.Crc32);
    }

    [Fact]
    public void GameToneMappingParametersAreRead()
    {
        var file = GameFiles.ToneMapping;
        if (file == null)
            return;

        Assert.Equal(1512, file.Bytecode.Length);
        Assert.Equal(
            [new ShaderResource("cCommonTexParam", 0x9262F48B, 0, 1), new ShaderResource("cToneMapParam", 0xA1D07C09, 1, 2)],
            file.ConstantBuffers);
        Assert.Equal(["sInput", "sToneMap"], file.Samplers.Select(s => s.Name));
        Assert.Equal(["sInput", "sToneMap"], file.Textures.Select(t => t.Name));
    }

    [Fact]
    public void OtherFilesAreRejected()
    {
        Assert.Throws<FormatException>(() => ShaderCodeFile.Parse(new byte[64]));
        Assert.Throws<FormatException>(() => ShaderCodeFile.Parse("ShCd"u8.ToArray()));
    }

    [Fact]
    public void Crc32MatchesTheStandardValue()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }
}
