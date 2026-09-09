using FluentAssertions;
using VideoOzet.Business.Helpers;

namespace VideoOzet.UnitTests.Business.Helpers;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ShouldReturnHashedPassword()
    {
        // Arrange
        var password = "MySuperSecretPassword123!";

        // Act
        var hash = PasswordHasher.Hash(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password);
    }

    [Fact]
    public void Verify_ShouldReturnTrue_WhenPasswordMatchesHash()
    {
        // Arrange
        var password = "MySuperSecretPassword123!";
        var hash = PasswordHasher.Hash(password);

        // Act
        var result = PasswordHasher.Verify(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenPasswordDoesNotMatchHash()
    {
        // Arrange
        var password = "MySuperSecretPassword123!";
        var wrongPassword = "WrongPassword123!";
        var hash = PasswordHasher.Hash(password);

        // Act
        var result = PasswordHasher.Verify(wrongPassword, hash);

        // Assert
        result.Should().BeFalse();
    }
}
