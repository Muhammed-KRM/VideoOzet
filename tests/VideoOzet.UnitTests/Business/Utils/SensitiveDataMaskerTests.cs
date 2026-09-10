using FluentAssertions;
using VideoOzet.Business.Utils;
using Xunit;

namespace VideoOzet.UnitTests.Business.Utils;

public class SensitiveDataMaskerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MaskJson_ShouldReturnInput_WhenNullOrEmpty(string? input)
    {
        // Act
        var result = SensitiveDataMasker.MaskJson(input);

        // Assert
        result.Should().Be(input);
    }

    [Fact]
    public void MaskJson_ShouldMaskSensitiveKeys()
    {
        // Arrange
        var json = "{\"username\":\"admin\",\"password\":\"supersecret123\",\"token\":\"jwt.token.here\",\"apiKey\":\"sk-ant-12345\"}";

        // Act
        var masked = SensitiveDataMasker.MaskJson(json);

        // Assert
        masked.Should().NotBeNull();
        masked.Should().Contain("\"username\":\"admin\"");
        masked.Should().Contain("\"password\":\"***\"");
        masked.Should().Contain("\"token\":\"***\"");
        masked.Should().Contain("\"apiKey\":\"***\"");
        masked.Should().NotContain("supersecret123");
        masked.Should().NotContain("jwt.token.here");
        masked.Should().NotContain("sk-ant-12345");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("Password")]
    [InlineData("token")]
    [InlineData("TOKEN")]
    [InlineData("refreshToken")]
    [InlineData("aesKey")]
    [InlineData("ibanEncrypted")]
    [InlineData("tcknEncrypted")]
    [InlineData("apiKey")]
    [InlineData("APIKEY")]
    public void MaskJson_ShouldBeCaseInsensitive_ForSupportedKeys(string keyName)
    {
        // Arrange
        var json = $"{{\"{keyName}\": \"sensitive-value-999\"}}";

        // Act
        var masked = SensitiveDataMasker.MaskJson(json);

        // Assert
        masked.Should().Contain($"\"{keyName}\":\"***\"");
        masked.Should().NotContain("sensitive-value-999");
    }

    [Fact]
    public void MaskJson_ShouldHandleSpacesAroundColon()
    {
        // Arrange
        var json = "{\"password\"   :   \"secret_val\"}";

        // Act
        var masked = SensitiveDataMasker.MaskJson(json);

        // Assert
        masked.Should().Be("{\"password\":\"***\"}");
    }

    [Fact]
    public void MaskJson_ShouldNotModifyNonSensitiveFields()
    {
        // Arrange
        var json = "{\"title\":\"My Course\",\"description\":\"All about microservices\",\"views\":1500}";

        // Act
        var masked = SensitiveDataMasker.MaskJson(json);

        // Assert
        masked.Should().Be(json);
    }
}
