using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.API.Middleware;
using VideoOzet.Business.Exceptions;
using Xunit;

namespace VideoOzet.UnitTests.API.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _mockLogger;

    public ExceptionHandlingMiddlewareTests()
    {
        _mockLogger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenNoExceptionOccurs()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ExceptionHandlingMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn400_WhenValidationExceptionOccurs()
    {
        // Arrange
        var failures = new List<ValidationFailure>
        {
            new ValidationFailure("Baslik", "Başlık zorunludur.")
        };
        RequestDelegate next = (ctx) => throw new ValidationException(failures);

        var middleware = new ExceptionHandlingMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Contain("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        using var json = JsonDocument.Parse(responseText);
        json.RootElement.GetProperty("Title").GetString().Should().Be("Validation Error");
        json.RootElement.GetProperty("Errors")[0].GetProperty("ErrorMessage").GetString().Should().Be("Başlık zorunludur.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn404_WhenNotFoundExceptionOccurs()
    {
        // Arrange
        RequestDelegate next = (ctx) => throw new NotFoundException("Eğitim", Guid.NewGuid());

        var middleware = new ExceptionHandlingMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(404);
        context.Response.ContentType.Should().Contain("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        using var json = JsonDocument.Parse(responseText);
        json.RootElement.GetProperty("Title").GetString().Should().Be("Not Found");
        json.RootElement.GetProperty("Detail").GetString().Should().Contain("Eğitim");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn400_WhenBusinessExceptionOccurs()
    {
        // Arrange
        RequestDelegate next = (ctx) => throw new BusinessException("Bu işlem gerçekleştirilemez.");

        var middleware = new ExceptionHandlingMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Contain("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        using var json = JsonDocument.Parse(responseText);
        json.RootElement.GetProperty("Title").GetString().Should().Be("Business Rule Violation");
        json.RootElement.GetProperty("Detail").GetString().Should().Be("Bu işlem gerçekleştirilemez.");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn500_WhenUnexpectedExceptionOccurs()
    {
        // Arrange
        RequestDelegate next = (ctx) => throw new InvalidOperationException("Unexpected internal failure.");

        var middleware = new ExceptionHandlingMiddleware(next, _mockLogger.Object);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500);
        context.Response.ContentType.Should().Contain("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        using var json = JsonDocument.Parse(responseText);
        json.RootElement.GetProperty("Title").GetString().Should().Be("Internal Server Error");
    }
}
