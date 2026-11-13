using FluentAssertions;
using VideoOzet.Business.Helpers;
using Xunit;

namespace VideoOzet.UnitTests.Business.Helpers;

public class DocumentContentValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ...   ")]
    public void IsBlockedOrInvalidContent_ShouldReturnTrue_ForEmptyOrTinyText(string? input)
    {
        var invalid = DocumentContentValidator.IsBlockedOrInvalidContent(input, out var reason);

        invalid.Should().BeTrue();
        reason.Should().Contain("okunabilir metin");
    }

    [Fact]
    public void IsBlockedOrInvalidContent_ShouldReturnTrue_ForCloudflareBlockPage()
    {
        var cloudflareText = @"
            Sorry, you have been blocked
            You are unable to access islamansiklopedisi.org.tr
            Why have I been blocked? This website is using a security service to protect itself from online attacks.
            Cloudflare Ray ID: a562c43f0e8d06e0
            Performance & security by Cloudflare
        ";

        var invalid = DocumentContentValidator.IsBlockedOrInvalidContent(cloudflareText, out var reason);

        invalid.Should().BeTrue();
        reason.Should().Contain("Cloudflare");
    }

    [Fact]
    public void IsBlockedOrInvalidContent_ShouldReturnFalse_ForAcademicArticleMentionsCloudflare()
    {
        // Uzun bir akademik makalede "cloudflare" veya "access denied" kelimesi geçse bile blok sayfası sayılmamalı
        var words = new string[650];
        for (int i = 0; i < words.Length; i++) words[i] = "algoritma";
        words[10] = "cloudflare";
        words[20] = "access";
        words[21] = "denied";

        var validArticle = string.Join(" ", words);
        var invalid = DocumentContentValidator.IsBlockedOrInvalidContent(validArticle, out var reason);

        invalid.Should().BeFalse();
        reason.Should().BeEmpty();
    }
}
