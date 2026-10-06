using VideoOzet.Business.Helpers;
using Xunit;

namespace VideoOzet.UnitTests.Business.Helpers;

public class TextNormalizerTests
{
    [Fact]
    public void NormalizeTurkishText_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, TextNormalizer.NormalizeTurkishText(null));
        Assert.Equal(string.Empty, TextNormalizer.NormalizeTurkishText(""));
        Assert.Equal(string.Empty, TextNormalizer.NormalizeTurkishText("   "));
    }

    [Fact]
    public void NormalizeTurkishText_ReplacesLegacyFontCorruptions()
    {
        var input = "ÜNøTE 8 ÖNERME. Önermenin TanÕPÕ ve Çeúitleri. ùartlÕ önermeler ve tanÕmlar. Bilindi÷i gibi.";
        var result = TextNormalizer.NormalizeTurkishText(input);

        Assert.Contains("ÜNİTE", result);
        Assert.Contains("Tanımı", result);
        Assert.Contains("Çeşitleri", result);
        Assert.Contains("Şartlı", result);
        Assert.Contains("tanımlar", result);
        Assert.Contains("Bilindiği", result);
    }

    [Fact]
    public void NormalizeTurkishText_RemovesControlCharactersAndExtraWhitespace()
    {
        var input = "Bu\x00 bir\x07 test   metnidir.\n\n\n\nYeni paragraf.";
        var result = TextNormalizer.NormalizeTurkishText(input);

        Assert.DoesNotContain('\0', result);
        Assert.DoesNotContain('\a', result);
        Assert.Equal("Bu bir test metnidir.\n\nYeni paragraf.", result);
    }
}
