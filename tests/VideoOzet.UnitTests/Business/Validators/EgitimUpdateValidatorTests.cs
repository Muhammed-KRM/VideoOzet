using FluentAssertions;
using System;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Validators;

namespace VideoOzet.UnitTests.Business.Validators;

public class EgitimUpdateValidatorTests
{
    private readonly EgitimUpdateValidator _validator;

    public EgitimUpdateValidatorTests()
    {
        _validator = new EgitimUpdateValidator();
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenIdIsEmpty()
    {
        // Arrange
        var dto = new EgitimUpdateDto { Id = Guid.Empty, Ad = "Test", Aciklama = "Test" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Id" && x.ErrorMessage == "Eğitim ID boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenAdIsEmpty()
    {
        // Arrange
        var dto = new EgitimUpdateDto { Id = Guid.NewGuid(), Ad = "", Aciklama = "Test" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Ad" && x.ErrorMessage == "Eğitim adı boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldPass_WhenDtoIsValid()
    {
        // Arrange
        var dto = new EgitimUpdateDto { Id = Guid.NewGuid(), Ad = "Test Ad", Aciklama = "Geçerli açıklama" };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
