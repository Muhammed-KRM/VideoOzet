using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using VideoOzet.Business.Infrastructure.Cache;

namespace VideoOzet.UnitTests.Business.Infrastructure.Cache;

public class RedisCacheServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<RedisCacheService>> _mockLogger;
    private readonly RedisCacheService _service;

    public RedisCacheServiceTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<RedisCacheService>>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);
        _service = new RedisCacheService(_mockRedis.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SetIfNotExistsAsync_ShouldReturnTrue_WhenKeyDoesNotExist()
    {
        // Arrange
        var key = "testKey";
        var value = "testValue";
        var expiration = TimeSpan.FromMinutes(5);

        _mockDatabase.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
                     .ReturnsAsync(true);

        // Act
        var result = await _service.SetIfNotExistsAsync(key, value, expiration);

        // Assert
        result.Should().BeTrue();
        _mockDatabase.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()), Times.Once);
    }

    [Fact]
    public async Task SetIfNotExistsAsync_ShouldReturnFalse_WhenExceptionOccurs()
    {
        // Arrange
        _mockDatabase.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
                     .ThrowsAsync(new Exception("test"));

        // Act
        var result = await _service.SetIfNotExistsAsync("k", "v", TimeSpan.FromMinutes(1));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnValue_WhenKeyExists()
    {
        // Arrange
        var key = "testKey";
        var expectedValue = "testValue";
        _mockDatabase.Setup(d => d.StringGetAsync(key, It.IsAny<CommandFlags>())).ReturnsAsync((RedisValue)expectedValue);

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        result.Should().Be(expectedValue);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenKeyDoesNotExist()
    {
        // Arrange
        var key = "testKey";
        _mockDatabase.Setup(d => d.StringGetAsync(key, It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _service.GetAsync(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_ShouldCallKeyDelete()
    {
        // Arrange
        var key = "testKey";

        // Act
        await _service.RemoveAsync(key);

        // Assert
        _mockDatabase.Verify(d => d.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
    }
}
