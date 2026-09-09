using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VideoOzet.API.Controllers;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.UnitTests.API.Controllers;

public class VideolarControllerTests
{
    private readonly Mock<IVideoService> _mockVideoService;
    private readonly VideolarController _controller;

    public VideolarControllerTests()
    {
        _mockVideoService = new Mock<IVideoService>();
        _controller = new VideolarController(_mockVideoService.Object);
    }

    [Fact]
    public async Task GetVideosByEgitim_ShouldReturnOkWithList()
    {
        // Arrange
        var egitimId = Guid.NewGuid();
        var dtoList = new List<VideoListDto> { new VideoListDto { Id = Guid.NewGuid() } };
        _mockVideoService.Setup(s => s.GetVideosByEgitimIdAsync(egitimId)).ReturnsAsync(dtoList);

        // Act
        var result = await _controller.GetVideosByEgitim(egitimId);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        okResult.Value.Should().BeEquivalentTo(dtoList);
    }

    [Fact]
    public async Task UploadVideo_ShouldReturnOk_WhenFileIsValid()
    {
        // Arrange
        var egitimId = Guid.NewGuid();
        var mockFile = new Mock<IFormFile>();
        var content = "dummy video content";
        var fileName = "test.mp4";
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms);
        writer.Write(content);
        writer.Flush();
        ms.Position = 0;

        mockFile.Setup(f => f.OpenReadStream()).Returns(ms);
        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(ms.Length);
        mockFile.Setup(f => f.ContentType).Returns("video/mp4");

        var listDto = new VideoListDto { Id = Guid.NewGuid() };
        _mockVideoService.Setup(s => s.UploadVideoAsync(It.IsAny<VideoUploadDto>())).ReturnsAsync(listDto);

        // Act
        var result = await _controller.UploadVideo(egitimId, mockFile.Object);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        okResult.Value.Should().BeEquivalentTo(listDto);
    }

    [Fact]
    public async Task UploadVideo_ShouldReturnBadRequest_WhenFileIsNull()
    {
        // Arrange
        var egitimId = Guid.NewGuid();

        // Act
        var result = await _controller.UploadVideo(egitimId, null!);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task DeleteVideo_ShouldReturnNoContent()
    {
        // Arrange
        var egitimId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        _mockVideoService.Setup(s => s.DeleteVideoAsync(videoId)).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DeleteVideo(egitimId, videoId);

        // Assert
        var noContentResult = result as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(204);
    }
}
