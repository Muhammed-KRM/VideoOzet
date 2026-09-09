using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Moq;
using VideoOzet.Business.Infrastructure.Storage;

namespace VideoOzet.UnitTests.Business.Infrastructure.Storage;

public class MinioFileServiceTests
{
    private readonly Mock<IMinioClient> _mockMinioClient;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<MinioFileService>> _mockLogger;
    private readonly MinioFileService _service;

    public MinioFileServiceTests()
    {
        _mockMinioClient = new Mock<IMinioClient>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<MinioFileService>>();

        _mockConfig.Setup(c => c.GetSection(It.IsAny<string>())).Returns(new Mock<IConfigurationSection>().Object);

        _service = new MinioFileService(_mockMinioClient.Object, _mockConfig.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task UploadFileAsync_ShouldUploadFile_WhenBucketExists()
    {
        // Arrange
        var stream = new MemoryStream(new byte[10]);
        _mockMinioClient.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(true);
        _mockMinioClient.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PutObjectResponse(System.Net.HttpStatusCode.OK, "test.mp4", new System.Collections.Generic.Dictionary<string, string>(), 10, "tag"));

        // Act
        var result = await _service.UploadFileAsync(stream, "test.mp4", "video/mp4");

        // Assert
        result.Should().Be("test.mp4");
        _mockMinioClient.Verify(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockMinioClient.Verify(m => m.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockMinioClient.Verify(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_ShouldCreateBucketAndUploadFile_WhenBucketDoesNotExist()
    {
        // Arrange
        var stream = new MemoryStream(new byte[10]);
        _mockMinioClient.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(false);
        _mockMinioClient.Setup(m => m.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
        _mockMinioClient.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PutObjectResponse(System.Net.HttpStatusCode.OK, "test.mp4", new System.Collections.Generic.Dictionary<string, string>(), 10, "tag"));

        // Act
        var result = await _service.UploadFileAsync(stream, "test.mp4", "video/mp4");

        // Assert
        result.Should().Be("test.mp4");
        _mockMinioClient.Verify(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockMinioClient.Verify(m => m.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockMinioClient.Verify(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldCallRemoveObject()
    {
        // Arrange
        _mockMinioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteFileAsync("test.mp4");

        // Assert
        _mockMinioClient.Verify(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFileUrlAsync_ShouldReturnUrl()
    {
        // Arrange
        var expectedUrl = "http://minio/bucket/test.mp4";
        _mockMinioClient.Setup(m => m.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
                        .ReturnsAsync(expectedUrl);

        // Act
        var result = await _service.GetFileUrlAsync("test.mp4");

        // Assert
        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task UploadFileAsync_ShouldThrowException_WhenMinioFails()
    {
        // Arrange
        var stream = new MemoryStream(new byte[10]);
        _mockMinioClient.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Exception("MinIO failure"));

        // Act
        var act = () => _service.UploadFileAsync(stream, "test.mp4", "video/mp4");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("File upload failed");
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldThrowException_WhenMinioFails()
    {
        // Arrange
        _mockMinioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Exception("MinIO failure"));

        // Act
        var act = () => _service.DeleteFileAsync("test.mp4");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("File deletion failed");
    }

    [Fact]
    public async Task GetFileUrlAsync_ShouldThrowException_WhenMinioFails()
    {
        // Arrange
        _mockMinioClient.Setup(m => m.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
                        .ThrowsAsync(new Exception("MinIO failure"));

        // Act
        var act = () => _service.GetFileUrlAsync("test.mp4");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("URL generation failed");
    }

    [Fact]
    public async Task DownloadFileAsync_ShouldThrowException_WhenMinioFails()
    {
        // Arrange
        _mockMinioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Exception("MinIO failure"));

        // Act
        var act = () => _service.DownloadFileAsync("test.mp4");

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("File download failed");
    }
}
