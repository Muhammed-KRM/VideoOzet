using FluentAssertions;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Validators;
using Xunit;

namespace VideoOzet.UnitTests.Business.Validators;

public class UserRegisterValidatorTests
{
    private readonly UserRegisterValidator _validator;

    public UserRegisterValidatorTests()
    {
        _validator = new UserRegisterValidator();
    }

    [Theory]
    [InlineData("", "E-posta adresi zorunludur.")]
    [InlineData("gecersiz_email", "Geçerli bir e-posta adresi giriniz.")]
    public void Validate_EmailIsInvalid_ReturnsError(string email, string expectedError)
    {
        // Arrange
        var dto = new UserRegisterDto { Email = email, Password = "Password123!", FullName = "Test User" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email" && e.ErrorMessage == expectedError);
    }

    [Theory]
    [InlineData("", "Şifre zorunludur.")]
    [InlineData("kisa", "Şifre en az 8 karakter olmalıdır.")]
    [InlineData("sifre1234", "Şifre en az bir büyük harf içermelidir.")]
    [InlineData("SIFRE1234", "Şifre en az bir küçük harf içermelidir.")]
    [InlineData("SifreZorlu", "Şifre en az bir rakam içermelidir.")]
    public void Validate_PasswordIsInvalid_ReturnsError(string password, string expectedError)
    {
        // Arrange
        var dto = new UserRegisterDto { Email = "test@test.com", Password = password, FullName = "Test User" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password" && e.ErrorMessage == expectedError);
    }

    [Fact]
    public void Validate_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var dto = new UserRegisterDto 
        { 
            Email = "gerekli@test.com", 
            Password = "Password123!", 
            FullName = "Muhammed" 
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
