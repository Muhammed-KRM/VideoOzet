using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace VideoOzet.IntegrationTests;

public class ApiKeyAuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiKeyAuthTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<VideoOzet.Data.Context.AppDbContext>(scope.ServiceProvider);
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetEgitimler_WithoutApiKey_ShouldReturnUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/egitimler");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetEgitimler_WithInvalidApiKey_ShouldReturnUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/egitimler");
        request.Headers.Add("X-API-Key", "invalid_key");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetEgitimler_WithValidApiKey_ShouldNotReturnUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/egitimler");
        request.Headers.Add("X-API-Key", "test-valid-key");
        var response = await _client.SendAsync(request);
        
        // As long as it is not 401, the auth middleware worked.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
