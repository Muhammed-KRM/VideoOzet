using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.UnitTests.Data.Repositories;

public class GenericRepositoryTests
{
    private readonly Mock<AppDbContext> _mockDbContext;

    public GenericRepositoryTests()
    {
        var options = new DbContextOptions<AppDbContext>();
        _mockDbContext = new Mock<AppDbContext>(options);
    }

    [Fact]
    public async Task AddAsync_ShouldAddEntity()
    {
        // Arrange
        var egitim = new Egitim { Id = Guid.NewGuid(), Ad = "Test" };
        var mockSet = new Mock<DbSet<Egitim>>();
        _mockDbContext.Setup(m => m.Set<Egitim>()).Returns(mockSet.Object);
        var repository = new GenericRepository<Egitim>(_mockDbContext.Object);

        // Act
        await repository.AddAsync(egitim);

        // Assert
        mockSet.Verify(m => m.AddAsync(egitim, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllEntities()
    {
        // Arrange
        var egitimler = new List<Egitim>
        {
            new Egitim { Id = Guid.NewGuid(), Ad = "E1" },
            new Egitim { Id = Guid.NewGuid(), Ad = "E2" }
        };
        _mockDbContext.Setup(m => m.Set<Egitim>()).ReturnsDbSet(egitimler);
        var repository = new GenericRepository<Egitim>(_mockDbContext.Object);

        // Act
        var result = await repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Update_ShouldUpdateEntity()
    {
        // Arrange
        var egitim = new Egitim { Id = Guid.NewGuid(), Ad = "Old" };
        var mockSet = new Mock<DbSet<Egitim>>();
        _mockDbContext.Setup(m => m.Set<Egitim>()).Returns(mockSet.Object);
        var repository = new GenericRepository<Egitim>(_mockDbContext.Object);

        // Act
        repository.Update(egitim);

        // Assert
        mockSet.Verify(m => m.Update(egitim), Times.Once);
    }

    [Fact]
    public void Delete_ShouldRemoveEntity()
    {
        // Arrange
        var egitim = new Egitim { Id = Guid.NewGuid(), Ad = "To Delete" };
        var mockSet = new Mock<DbSet<Egitim>>();
        _mockDbContext.Setup(m => m.Set<Egitim>()).Returns(mockSet.Object);
        var repository = new GenericRepository<Egitim>(_mockDbContext.Object);

        // Act
        repository.Delete(egitim);

        // Assert
        mockSet.Verify(m => m.Remove(egitim), Times.Once);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldCallDbContext()
    {
        // Arrange
        _mockDbContext.Setup(m => m.Set<Egitim>()).Returns(new Mock<DbSet<Egitim>>().Object);
        var repository = new GenericRepository<Egitim>(_mockDbContext.Object);
        _mockDbContext.Setup(m => m.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await repository.SaveChangesAsync();

        // Assert
        result.Should().Be(1);
        _mockDbContext.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
