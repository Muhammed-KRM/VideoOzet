using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class LogManagerTests
{
    private readonly Mock<AppDbContext> _mockContext;
    private readonly Mock<DbSet<EndpointLog>> _mockEndpointLogs;
    private readonly Mock<DbSet<PipelineLog>> _mockPipelineLogs;
    private readonly Mock<DbSet<FunctionLog>> _mockFunctionLogs;
    private readonly Mock<ILogger<LogManager>> _mockLogger;
    private readonly List<EndpointLog> _endpointLogsList;
    private readonly List<PipelineLog> _pipelineLogsList;
    private readonly List<FunctionLog> _functionLogsList;
    private readonly LogManager _logManager;
    private long _nextPipelineLogId = 1;

    public LogManagerTests()
    {
        _mockContext = new Mock<AppDbContext>(new DbContextOptions<AppDbContext>());
        _mockLogger = new Mock<ILogger<LogManager>>();

        _mockEndpointLogs = new Mock<DbSet<EndpointLog>>();
        _mockPipelineLogs = new Mock<DbSet<PipelineLog>>();
        _mockFunctionLogs = new Mock<DbSet<FunctionLog>>();

        _endpointLogsList = new List<EndpointLog>();
        _pipelineLogsList = new List<PipelineLog>();
        _functionLogsList = new List<FunctionLog>();

        _mockEndpointLogs.Setup(x => x.Add(It.IsAny<EndpointLog>()))
            .Callback<EndpointLog>(log => _endpointLogsList.Add(log));

        _mockPipelineLogs.Setup(x => x.Add(It.IsAny<PipelineLog>()))
            .Callback<PipelineLog>(log =>
            {
                log.Id = _nextPipelineLogId++;
                _pipelineLogsList.Add(log);
            });

        _mockPipelineLogs.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((object[] keyValues) =>
            {
                var id = (long)keyValues[0];
                return _pipelineLogsList.FirstOrDefault(l => l.Id == id);
            });

        _mockPipelineLogs.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object[] keyValues, CancellationToken ct) =>
            {
                var id = (long)keyValues[0];
                return _pipelineLogsList.FirstOrDefault(l => l.Id == id);
            });

        _mockFunctionLogs.Setup(x => x.Add(It.IsAny<FunctionLog>()))
            .Callback<FunctionLog>(log => _functionLogsList.Add(log));

        _mockContext.Setup(x => x.EndpointLogs).ReturnsDbSet(_endpointLogsList, _mockEndpointLogs);
        _mockContext.Setup(x => x.PipelineLogs).ReturnsDbSet(_pipelineLogsList, _mockPipelineLogs);
        _mockContext.Setup(x => x.FunctionLogs).ReturnsDbSet(_functionLogsList, _mockFunctionLogs);

        _logManager = new LogManager(_mockContext.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task LogEndpointAsync_ShouldSaveEndpointLog_WithMaskedRequestBody()
    {
        // Arrange
        var entry = new EndpointLogEntry
        {
            TraceId = "trace-123",
            Method = "POST",
            Path = "/api/v1/auth/login",
            Query = "?ref=home",
            RequestBody = "{\"username\":\"admin\",\"password\":\"supersecret123\"}",
            ResponseBody = "{\"token\":\"jwt-xyz\"}",
            StatusCode = 200,
            IpAddress = "127.0.0.1",
            UserAgent = "Mozilla/5.0",
            DurationMs = 45
        };

        // Act
        await _logManager.LogEndpointAsync(entry);

        // Assert
        _endpointLogsList.Should().HaveCount(1);
        var savedLog = _endpointLogsList[0];
        savedLog.Method.Should().Be("POST");
        savedLog.Path.Should().Be("/api/v1/auth/login");
        savedLog.StatusCode.Should().Be(200);
        savedLog.RequestBody.Should().Contain("\"password\":\"***\"");
        savedLog.RequestBody.Should().NotContain("supersecret123");
        _mockContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogPipelineStartAsync_ShouldCreatePipelineLog_AndReturnGeneratedId()
    {
        // Arrange
        var videoId = Guid.NewGuid();
        var egitimId = Guid.NewGuid();

        // Act
        var logId = await _logManager.LogPipelineStartAsync(videoId, egitimId, PipelineAsamasi.Sentez, "trace-abc");

        // Assert
        logId.Should().BeGreaterThan(0);
        _pipelineLogsList.Should().HaveCount(1);
        var savedLog = _pipelineLogsList[0];
        savedLog.VideoId.Should().Be(videoId);
        savedLog.EgitimId.Should().Be(egitimId);
        savedLog.Asama.Should().Be(PipelineAsamasi.Sentez);
        savedLog.Durum.Should().Be("Basladi");
        savedLog.TraceId.Should().Be("trace-abc");
        _mockContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogPipelineEndAsync_ShouldUpdatePipelineLog_WithDurationAndStatus()
    {
        // Arrange
        var logId = await _logManager.LogPipelineStartAsync(Guid.NewGuid(), Guid.NewGuid(), PipelineAsamasi.KaliteKontrol);

        // Act
        await Task.Delay(10); // Ensure some duration
        await _logManager.LogPipelineEndAsync(logId, "{\"result\":\"success\",\"apiKey\":\"secret-key\"}");

        // Assert
        var updatedLog = _pipelineLogsList.FirstOrDefault(l => l.Id == logId);
        updatedLog.Should().NotBeNull();
        updatedLog!.Durum.Should().Be("Tamamlandi");
        updatedLog.BitisZamani.Should().NotBeNull();
        updatedLog.SureMs.Should().BeGreaterThanOrEqualTo(0);
        updatedLog.CiktiMetadata.Should().Contain("\"apiKey\":\"***\"");
        updatedLog.CiktiMetadata.Should().NotContain("secret-key");
        _mockContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LogPipelineErrorAsync_ShouldUpdatePipelineLog_WithErrorMessageAndStackTrace()
    {
        // Arrange
        var logId = await _logManager.LogPipelineStartAsync(Guid.NewGuid(), Guid.NewGuid(), PipelineAsamasi.Sentez);

        Exception testException;
        try
        {
            throw new InvalidOperationException("Something went wrong during synthesis.");
        }
        catch (Exception ex)
        {
            testException = ex;
        }

        // Act
        await _logManager.LogPipelineErrorAsync(logId, testException);

        // Assert
        var updatedLog = _pipelineLogsList.FirstOrDefault(l => l.Id == logId);
        updatedLog.Should().NotBeNull();
        updatedLog!.Durum.Should().Be("Hata");
        updatedLog.HataMesaji.Should().Be("Something went wrong during synthesis.");
        updatedLog.HataDetayi.Should().NotBeNullOrEmpty();
        _mockContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LogFunctionErrorAsync_ShouldSaveFunctionLog_WithMaskedParametersAndCallerInfo()
    {
        // Arrange
        var ex = new ArgumentException("Invalid arguments provided.");
        var parameters = new { Topic = "Microservices", password = "plain-text-pass" };

        // Act
        await _logManager.LogFunctionErrorAsync(
            functionName: nameof(LogFunctionErrorAsync_ShouldSaveFunctionLog_WithMaskedParametersAndCallerInfo),
            ex: ex,
            parameters: parameters,
            traceId: "trace-xyz",
            errorCode: "TEST_ERR");

        // Assert
        _functionLogsList.Should().HaveCount(1);
        var savedLog = _functionLogsList[0];
        savedLog.ErrorCode.Should().Be("TEST_ERR");
        savedLog.ErrorMessage.Should().Be("Invalid arguments provided.");
        savedLog.MethodName.Should().Be(nameof(LogFunctionErrorAsync_ShouldSaveFunctionLog_WithMaskedParametersAndCallerInfo));
        savedLog.InputValue.Should().Contain("\"password\":\"***\"");
        savedLog.InputValue.Should().NotContain("plain-text-pass");
        savedLog.Severity.Should().Be("Error");
        _mockContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
