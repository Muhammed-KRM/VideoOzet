using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.Infrastructure.AI;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class ClaudeSynthesisProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<ClaudeSynthesisProvider>> _mockLogger;
    private readonly ClaudeSynthesisProvider _provider;

    public ClaudeSynthesisProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ClaudeSynthesisProvider>>();

        // API Key is missing/empty
        _mockConfig.Setup(c => c["ANTHROPIC_API_KEY"]).Returns(string.Empty);
        _mockConfig.Setup(c => c["CLAUDE_MODEL"]).Returns("claude-3-5-sonnet-20240620");

        _provider = new ClaudeSynthesisProvider(_mockLogger.Object, _mockConfig.Object);
    }

    [Fact]
    public async Task GenerateResearchSummaryAsync_ShouldReturnMockResponse_WhenApiKeyIsMissing()
    {
        // Act
        var result = await _provider.GenerateResearchSummaryAsync("C# Microservices", "Orta Uzunluk", "Yazılımcılar", "Context data", CancellationToken.None);

        // Assert
        result.Should().Be("Mock Claude Response");
    }

    [Fact]
    public async Task GenerateVideoPlanAsync_ShouldReturnMockResponse_WhenApiKeyIsMissing()
    {
        // Act
        var result = await _provider.GenerateVideoPlanAsync("C# Microservices", "10 Dakika", "Context data", CancellationToken.None);

        // Assert
        result.Should().Be("Mock Claude Response");
    }

    [Fact]
    public async Task ExtractClaimsAsync_ShouldReturnMockResponse_WhenApiKeyIsMissing()
    {
        // Act
        var result = await _provider.ExtractClaimsAsync("Some summary text with facts", CancellationToken.None);

        // Assert
        result.Should().Be("Mock Claude Response");
    }

    [Fact]
    public async Task VerifyClaimAsync_ShouldReturnMockResponse_WhenApiKeyIsMissing()
    {
        // Act
        var result = await _provider.VerifyClaimAsync("Claim 1", "Context data", CancellationToken.None);

        // Assert
        result.Should().Be("Mock Claude Response");
    }
}
