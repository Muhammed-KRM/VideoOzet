using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using VideoOzet.API.Middleware;
using Xunit;

namespace VideoOzet.UnitTests.API.Middleware;

public class ApiKeyMiddlewareTests
{
    private readonly IConfiguration _config;
    private const string ValidApiKey = "test-valid-api-key-12345";

    public ApiKeyMiddlewareTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ApiKey", ValidApiKey }
            })
            .Build();
    }

    [Fact]
    public async Task InvokeAsync_ShouldBypassApiKeyCheck_WhenPathIsSwagger()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/swagger/index.html";

        // Act
        await middleware.InvokeAsync(context, _config);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ShouldBypassApiKeyCheck_WhenPathIsHealth()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        // Act
        await middleware.InvokeAsync(context, _config);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenApiKeyHeaderIsMissing()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/egitimler";
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context, _config);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        responseText.Should().Be("API Key was not provided.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenApiKeyIsInvalid()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/egitimler";
        context.Request.Headers["X-API-Key"] = "wrong-api-key";
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context, _config);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        responseText.Should().Be("Unauthorized client.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenApiKeyIsValid()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/egitimler";
        context.Request.Headers["X-API-Key"] = ValidApiKey;

        // Act
        await middleware.InvokeAsync(context, _config);

        // Assert
        nextCalled.Should().BeTrue();
    }
}
