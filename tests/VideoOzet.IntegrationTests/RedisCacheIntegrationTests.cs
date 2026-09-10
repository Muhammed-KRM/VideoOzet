using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.Interfaces;
using Xunit;

namespace VideoOzet.IntegrationTests;

public class RedisCacheIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RedisCacheIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Redis_SetIfNotExists_Get_And_Remove_Should_Work_Correctly()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var testKey = $"integration_test:{Guid.NewGuid():N}";
        var originalValue = "test_data_payload_123";

        // Act 1: SetIfNotExists (first time -> true)
        var setFirstTime = await cacheService.SetIfNotExistsAsync(testKey, originalValue, TimeSpan.FromMinutes(2));
        Assert.True(setFirstTime, "First SetIfNotExists should return true.");

        // Act 2: SetIfNotExists (second time -> false)
        var setSecondTime = await cacheService.SetIfNotExistsAsync(testKey, "another_value", TimeSpan.FromMinutes(2));
        Assert.False(setSecondTime, "Second SetIfNotExists on existing key should return false.");

        // Act 3: Get
        var fetchedValue = await cacheService.GetAsync(testKey);
        Assert.Equal(originalValue, fetchedValue);

        // Act 4: Remove
        await cacheService.RemoveAsync(testKey);

        // Act 5: Verify Removed
        var afterRemoveValue = await cacheService.GetAsync(testKey);
        Assert.Null(afterRemoveValue);
    }
}
