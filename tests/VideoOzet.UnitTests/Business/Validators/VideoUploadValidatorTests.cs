using FluentAssertions;
using System;
using System.IO;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Validators;

namespace VideoOzet.UnitTests.Business.Validators;

public class VideoUploadValidatorTests
{
    private readonly VideoUploadValidator _validator;

    public VideoUploadValidatorTests()
    {
        _validator = new VideoUploadValidator();
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenEgitimIdIsEmpty()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        var dto = new VideoUploadDto { EgitimId = Guid.Empty, FileName = "test.mp4", ContentType = "video/mp4", FileStream = stream };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "EgitimId" && x.ErrorMessage == "Eğitim ID boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenFileNameIsEmpty()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid(), FileName = "", ContentType = "video/mp4", FileStream = stream };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "FileName" && x.ErrorMessage == "Dosya adı boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenContentTypeIsNotVideo()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid(), FileName = "test.txt", ContentType = "text/plain", FileStream = stream };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "ContentType" && x.ErrorMessage == "Sadece video dosyaları yüklenebilir.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenFileStreamIsNull()
    {
        // Arrange
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid(), FileName = "test.mp4", ContentType = "video/mp4", FileStream = null! };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "FileStream" && x.ErrorMessage == "Dosya içeriği boş olamaz.");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenFileStreamIsEmpty()
    {
        // Arrange
        using var stream = new MemoryStream(); // Empty stream
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid(), FileName = "test.mp4", ContentType = "video/mp4", FileStream = stream };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "FileStream" && x.ErrorMessage == "Dosya boyutu 0'dan büyük olmalıdır.");
    }

    [Fact]
    public void Validate_ShouldPass_WhenDtoIsValid()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid(), FileName = "test.mp4", ContentType = "video/mp4", FileStream = stream };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
