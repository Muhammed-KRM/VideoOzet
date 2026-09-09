using FluentAssertions;
using VideoOzet.Business.Infrastructure.Moderation;

namespace VideoOzet.UnitTests.Business.Infrastructure.Moderation;

public class TurkishTextNormalizerTests
{
    [Theory]
    [InlineData("Normal metin", "normal metin")]
    [InlineData("M\u0430tem\u0430tik", "matematik")] // Cyrillic 'a' (U+0430)
    [InlineData("sıfır beş üç iki", "0 5 3 2")]
    [InlineData("BİR İKİ üç dÖRt beş ALTı Yedi sEkiz dokuz sıfır", "1 2 3 4 5 6 7 8 9 0")]
    [InlineData("S\u0430htek\u0430r", "sahtekar")]
    public void Normalize_ShouldNormalizeTextCorrectly(string input, string expected)
    {
        // Act
        var result = TurkishTextNormalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
    }
}
