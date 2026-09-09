using FluentAssertions;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Validators;

namespace VideoOzet.UnitTests.Business.Validators;

public class EgitimCreateValidatorTests
{
    private readonly EgitimCreateValidator _validator;

    public EgitimCreateValidatorTests()
    {
        _validator = new EgitimCreateValidator();
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenAdIsEmpty()
    {
        // Arrange
        var dto = new EgitimCreateDto { Ad = "", Aciklama = "Test" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Ad" && x.ErrorMessage == "Eğitim adı boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenAdIsTooLong()
    {
        // Arrange
        var dto = new EgitimCreateDto { Ad = new string('A', 201), Aciklama = "Test" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Ad" && x.ErrorMessage == "Eğitim adı en fazla 200 karakter olabilir.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenAciklamaIsTooLong()
    {
        // Arrange
        var dto = new EgitimCreateDto { Ad = "Test Ad", Aciklama = new string('A', 1001) };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Aciklama" && x.ErrorMessage == "Açıklama en fazla 1000 karakter olabilir.");
    }

    [Fact]
    public void Validate_ShouldPass_WhenDtoIsValid()
    {
        // Arrange
        var dto = new EgitimCreateDto { Ad = "Test Ad", Aciklama = "Geçerli açıklama" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
