using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Services;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.UnitTests.Business.Services;

public class EgitimManagerTests
{
    private readonly Mock<IRepository<Egitim>> _mockEgitimRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<EgitimManager>> _mockLogger;
    private readonly EgitimManager _egitimManager;

    public EgitimManagerTests()
    {
        _mockEgitimRepository = new Mock<IRepository<Egitim>>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<EgitimManager>>();

        _egitimManager = new EgitimManager(
            _mockEgitimRepository.Object,
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnEgitimList()
    {
        // Arrange
        var egitimler = new List<Egitim> { new Egitim { Id = Guid.NewGuid(), Ad = "Test" } };
        var dtoList = new List<EgitimListDto> { new EgitimListDto { Id = egitimler[0].Id, Ad = "Test" } };

        _mockEgitimRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(egitimler);
        _mockMapper.Setup(m => m.Map<IEnumerable<EgitimListDto>>(egitimler)).Returns(dtoList);

        // Act
        var result = await _egitimManager.GetAllAsync();

        // Assert
        result.Should().BeEquivalentTo(dtoList);
        _mockEgitimRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnEgitim_WhenEgitimExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        var egitim = new Egitim { Id = id, Ad = "Test" };
        var dto = new EgitimDetailDto { Id = id, Ad = "Test" };

        _mockEgitimRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(egitim);
        _mockMapper.Setup(m => m.Map<EgitimDetailDto>(egitim)).Returns(dto);

        // Act
        var result = await _egitimManager.GetByIdAsync(id);

        // Assert
        result.Should().BeEquivalentTo(dto);
        _mockEgitimRepository.Verify(r => r.GetByIdAsync(id), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFoundException_WhenEgitimDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockEgitimRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Egitim)null!);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _egitimManager.GetByIdAsync(id));
    }

    [Fact]
    public async Task CreateAsync_ShouldAddEgitimAndReturnDto()
    {
        // Arrange
        var createDto = new EgitimCreateDto { Ad = "New Egitim" };
        var egitim = new Egitim { Id = Guid.NewGuid(), Ad = "New Egitim" };
        var detailDto = new EgitimDetailDto { Id = egitim.Id, Ad = egitim.Ad, Durum = EgitimDurumu.Taslak };

        _mockMapper.Setup(m => m.Map<Egitim>(createDto)).Returns(egitim);
        _mockMapper.Setup(m => m.Map<EgitimDetailDto>(egitim)).Returns(detailDto);

        // Act
        var result = await _egitimManager.CreateAsync(createDto);

        // Assert
        egitim.Durum.Should().Be(EgitimDurumu.Taslak);
        _mockEgitimRepository.Verify(r => r.AddAsync(egitim), Times.Once);
        _mockEgitimRepository.Verify(r => r.SaveChangesAsync(), Times.Once);
        result.Should().BeEquivalentTo(detailDto);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateEgitimAndReturnDto()
    {
        // Arrange
        var updateDto = new EgitimUpdateDto { Id = Guid.NewGuid(), Ad = "Updated" };
        var egitim = new Egitim { Id = updateDto.Id, Ad = "Old" };
        var detailDto = new EgitimDetailDto { Id = updateDto.Id, Ad = "Updated" };

        _mockEgitimRepository.Setup(r => r.GetByIdAsync(updateDto.Id)).ReturnsAsync(egitim);
        _mockMapper.Setup(m => m.Map(updateDto, egitim)).Callback<EgitimUpdateDto, Egitim>((src, dest) => dest.Ad = src.Ad);
        _mockMapper.Setup(m => m.Map<EgitimDetailDto>(egitim)).Returns(detailDto);

        // Act
        var result = await _egitimManager.UpdateAsync(updateDto);

        // Assert
        egitim.Ad.Should().Be("Updated");
        _mockEgitimRepository.Verify(r => r.Update(egitim), Times.Once);
        _mockEgitimRepository.Verify(r => r.SaveChangesAsync(), Times.Once);
        result.Should().BeEquivalentTo(detailDto);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFoundException_WhenEgitimDoesNotExist()
    {
        // Arrange
        var updateDto = new EgitimUpdateDto { Id = Guid.NewGuid(), Ad = "Updated" };
        _mockEgitimRepository.Setup(r => r.GetByIdAsync(updateDto.Id)).ReturnsAsync((Egitim)null!);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _egitimManager.UpdateAsync(updateDto));
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteEgitim_WhenEgitimExists()
    {
        // Arrange
        var id = Guid.NewGuid();
        var egitim = new Egitim { Id = id };
        _mockEgitimRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(egitim);

        // Act
        await _egitimManager.DeleteAsync(id);

        // Assert
        _mockEgitimRepository.Verify(r => r.Delete(egitim), Times.Once);
        _mockEgitimRepository.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowNotFoundException_WhenEgitimDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockEgitimRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Egitim)null!);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _egitimManager.DeleteAsync(id));
    }
}
