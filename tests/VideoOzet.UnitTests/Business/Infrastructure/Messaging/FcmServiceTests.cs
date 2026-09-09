using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using VideoOzet.Business.Infrastructure.Messaging;

namespace VideoOzet.UnitTests.Business.Infrastructure.Messaging;

public class FcmServiceTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<FcmService>> _mockLogger;

    public FcmServiceTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<FcmService>>();
    }

    [Fact]
    public async Task SendNotificationAsync_ShouldNotSend_WhenDisabled()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"Firebase:Enabled", "false"}
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();
        var httpClient = new HttpClient();
        var service = new FcmService(httpClient, configuration, _mockLogger.Object);

        // Act
        await service.SendNotificationAsync("token", "title", "body");

        // Assert
        // We verify that it logged 'FCM devre dışı'
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FCM devre dışı")),
                It.IsAny<System.Exception>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendNotificationAsync_ShouldSend_WhenEnabledAndKeyExists()
    {
        // Arrange
        var mockSectionEnabled = new Mock<IConfigurationSection>();
        mockSectionEnabled.Setup(s => s.Value).Returns("true");
        _mockConfig.Setup(c => c.GetSection("Firebase:Enabled")).Returns(mockSectionEnabled.Object);
        _mockConfig.Setup(c => c["Firebase:ServerKey"]).Returns("test-key");

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("success")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var service = new FcmService(httpClient, _mockConfig.Object, _mockLogger.Object);

        // Act
        await service.SendNotificationAsync("token", "title", "body");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("başarıyla gönderildi")),
                It.IsAny<System.Exception>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.Once);
    }
}
