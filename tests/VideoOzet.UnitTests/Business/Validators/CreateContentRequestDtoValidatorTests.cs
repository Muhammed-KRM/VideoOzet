using FluentAssertions;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Validators;
using Xunit;

namespace VideoOzet.UnitTests.Business.Validators;

public class CreateContentRequestDtoValidatorTests
{
    private readonly CreateContentRequestDtoValidator _validator;

    public CreateContentRequestDtoValidatorTests()
    {
        _validator = new CreateContentRequestDtoValidator();
    }

    [Fact]
    public void Validate_ShouldPass_WhenDtoIsValid()
    {
        // Arrange
        var dto = new CreateContentRequestDto
        {
            Konu = "Mikroservis Mimarisi ve RabbitMQ",
            HedefUzunluk = "15 Dakika",
            HedefKitle = "Kıdemli Yazılımcılar"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldFail_WhenKonuIsEmpty(string? konu)
    {
        // Arrange
        var dto = new CreateContentRequestDto
        {
            Konu = konu!,
            HedefUzunluk = "10 Dakika"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateContentRequestDto.Konu));
    }

    [Fact]
    public void Validate_ShouldFail_WhenKonuExceedsMaxLength()
    {
        // Arrange
        var dto = new CreateContentRequestDto
        {
            Konu = new string('A', 10001)
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateContentRequestDto.Konu));
    }

    [Fact]
    public void Validate_ShouldFail_WhenHedefUzunlukExceedsMaxLength()
    {
        // Arrange
        var dto = new CreateContentRequestDto
        {
            Konu = "Test Konu",
            HedefUzunluk = new string('B', 101)
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateContentRequestDto.HedefUzunluk));
    }

    [Fact]
    public void Validate_ShouldFail_WhenHedefKitleExceedsMaxLength()
    {
        // Arrange
        var dto = new CreateContentRequestDto
        {
            Konu = "Test Konu",
            HedefKitle = new string('C', 101)
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateContentRequestDto.HedefKitle));
    }
}
