using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using VideoOzet.Business.Infrastructure.AI;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class GeminiSummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_ShouldReturnMockSummary_WhenApiKeyIsMissing()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["GEMINI_API_KEY"]).Returns(string.Empty);
        
        var mockLogger = new Mock<ILogger<GeminiSummarizer>>();

        var summarizer = new GeminiSummarizer(mockConfig.Object, mockLogger.Object);

        // Act
        var result = await summarizer.SummarizeAsync("Test transcript");

        // Assert
        result.Should().Contain("Mock Özet");
    }
}
