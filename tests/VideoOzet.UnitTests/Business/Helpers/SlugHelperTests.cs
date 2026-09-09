using FluentAssertions;
using VideoOzet.Business.Helpers;

namespace VideoOzet.UnitTests.Business.Helpers;

public class SlugHelperTests
{
    [Theory]
    [InlineData("Bu bir deneme", "bu-bir-deneme")]
    [InlineData("Türkçe Karakterler: ç, ğ, ı, ö, ş, ü", "turkce-karakterler-c-g-i-o-s-u")]
    [InlineData("  Boşluklar   ", "bosluklar")]
    [InlineData("Özel Karakterler!@#$%^&*()_+", "ozel-karakterler")]
    [InlineData("Multiple---Dashes", "multiple-dashes")]
    public void GenerateSlug_ShouldReturnCorrectSlug(string input, string expected)
    {
        // Act
        var result = SlugHelper.GenerateSlug(input);

        // Assert
        result.Should().Be(expected);
    }
}
