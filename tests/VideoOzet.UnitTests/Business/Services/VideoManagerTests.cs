using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.DTOs.Video;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.UnitTests.Business.Services;

public class VideoManagerTests
{
    private readonly Mock<IRepository<Video>> _mockVideoRepo;
    private readonly Mock<IRepository<Egitim>> _mockEgitimRepo;
    private readonly Mock<IFileStorageService> _mockFileService;
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<VideoManager>> _mockLogger;
    private readonly VideoManager _videoManager;

    public VideoManagerTests()
    {
        _mockVideoRepo = new Mock<IRepository<Video>>();
        _mockEgitimRepo = new Mock<IRepository<Egitim>>();
        _mockFileService = new Mock<IFileStorageService>();
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<VideoManager>>();

        _videoManager = new VideoManager(
            _mockVideoRepo.Object,
            _mockEgitimRepo.Object,
            _mockFileService.Object,
            _mockPublishEndpoint.Object,
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task UploadVideoAsync_ShouldUploadAndSaveVideoAndPublishEvent()
    {
        // Arrange
        var egitimId = Guid.NewGuid();
        var egitim = new Egitim { Id = egitimId, ToplamVideoSayisi = 0 };
        var dto = new VideoUploadDto
        {
            EgitimId = egitimId,
            FileName = "test.mp4",
            ContentType = "video/mp4",
            FileStream = new MemoryStream(new byte[100])
        };

        var uploadedPath = "minio-path/guid.mp4";
        var video = new Video { Id = Guid.NewGuid(), EgitimId = egitimId, DosyaYolu = uploadedPath, IslemDurumu = VideoIslemDurumu.Bekliyor };
        var listDto = new VideoListDto { Id = video.Id, DosyaYolu = uploadedPath };

        _mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);
        _mockFileService.Setup(f => f.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), dto.ContentType)).ReturnsAsync(uploadedPath);
        
        _mockVideoRepo.Setup(r => r.AddAsync(It.IsAny<Video>()))
            .Callback<Video>(v => v.Id = video.Id)
            .ReturnsAsync((Video v) => v);

        _mockMapper.Setup(m => m.Map<VideoListDto>(It.IsAny<Video>())).Returns(listDto);

        // Act
        var result = await _videoManager.UploadVideoAsync(dto);

        // Assert
        result.Should().BeEquivalentTo(listDto);
        egitim.ToplamVideoSayisi.Should().Be(1);
        _mockEgitimRepo.Verify(r => r.Update(egitim), Times.Once);
        _mockVideoRepo.Verify(r => r.AddAsync(It.IsAny<Video>()), Times.Once);
        _mockVideoRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _mockPublishEndpoint.Verify(p => p.Publish(It.IsAny<VideoUploadedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadVideoAsync_ShouldThrowNotFoundException_WhenEgitimDoesNotExist()
    {
        // Arrange
        var dto = new VideoUploadDto { EgitimId = Guid.NewGuid() };
        _mockEgitimRepo.Setup(r => r.GetByIdAsync(dto.EgitimId)).ReturnsAsync((Egitim)null!);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _videoManager.UploadVideoAsync(dto));
    }

    [Fact]
    public async Task GetVideosByEgitimIdAsync_ShouldReturnVideoList()
    {
        // Arrange
        var egitimId = Guid.NewGuid();
        var egitim = new Egitim { Id = egitimId };
        var videolar = new List<Video> { new Video { Id = Guid.NewGuid(), EgitimId = egitimId } };
        var dtoList = new List<VideoListDto> { new VideoListDto { Id = videolar[0].Id } };

        _mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);
        _mockVideoRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Video, bool>>>())).ReturnsAsync(videolar);
        _mockMapper.Setup(m => m.Map<IEnumerable<VideoListDto>>(videolar)).Returns(dtoList);

        // Act
        var result = await _videoManager.GetVideosByEgitimIdAsync(egitimId);

        // Assert
        result.Should().BeEquivalentTo(dtoList);
    }

    [Fact]
    public async Task DeleteVideoAsync_ShouldDeleteVideoAndFileAndDecreaseCount()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();
        var video = new Video { Id = videoId, EgitimId = egitimId, DosyaYolu = "test.mp4", IslemDurumu = VideoIslemDurumu.Tamamlandi };
        var egitim = new Egitim { Id = egitimId, ToplamVideoSayisi = 5, IslenmiVideoSayisi = 3 };

        _mockVideoRepo.Setup(r => r.GetByIdAsync(videoId)).ReturnsAsync(video);
        _mockEgitimRepo.Setup(r => r.GetByIdAsync(egitimId)).ReturnsAsync(egitim);

        // Act
        await _videoManager.DeleteVideoAsync(videoId);

        // Assert
        _mockFileService.Verify(f => f.DeleteFileAsync(video.DosyaYolu), Times.Once);
        egitim.ToplamVideoSayisi.Should().Be(4);
        egitim.IslenmiVideoSayisi.Should().Be(2); // Because video was Tamamlandi
        _mockEgitimRepo.Verify(r => r.Update(egitim), Times.Once);
        _mockVideoRepo.Verify(r => r.Delete(video), Times.Once);
        _mockVideoRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteVideoAsync_ShouldThrowNotFoundException_WhenVideoDoesNotExist()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        _mockVideoRepo.Setup(r => r.GetByIdAsync(videoId)).ReturnsAsync((Video)null!);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _videoManager.DeleteVideoAsync(videoId));
    }
}
