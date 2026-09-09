using FluentAssertions;
using VideoOzet.Business.Helpers;

namespace VideoOzet.UnitTests.Business.Helpers;

public class AesEncryptionHelperTests
{
    private const string TestKey = "my-32-character-ultra-secure-key!"; // 35 characters, will be padded/truncated by helper to 32
    
    [Fact]
    public void EncryptAndDecrypt_ShouldReturnOriginalText()
    {
        // Arrange
        var originalText = "This is a very sensitive piece of information.";

        // Act
        var encryptedText = AesEncryptionHelper.Encrypt(originalText, TestKey);
        var decryptedText = AesEncryptionHelper.Decrypt(encryptedText, TestKey);

        // Assert
        encryptedText.Should().NotBeNullOrEmpty();
        encryptedText.Should().NotBe(originalText);
        decryptedText.Should().Be(originalText);
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentCiphertextsForSameInput()
    {
        // Arrange
        var originalText = "This is a very sensitive piece of information.";

        // Act
        var encryptedText1 = AesEncryptionHelper.Encrypt(originalText, TestKey);
        var encryptedText2 = AesEncryptionHelper.Encrypt(originalText, TestKey);

        // Assert
        encryptedText1.Should().NotBe(encryptedText2); // Because IV is randomly generated each time
    }
}
