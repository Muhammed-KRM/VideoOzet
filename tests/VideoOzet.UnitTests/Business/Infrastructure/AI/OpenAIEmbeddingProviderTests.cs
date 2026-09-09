using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.Business.Infrastructure.AI;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class OpenAIEmbeddingProviderTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_ShouldReturnMockEmbeddings_WhenApiKeyIsMissing()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["OPENAI_API_KEY"]).Returns(string.Empty);
        
        var mockLogger = new Mock<ILogger<OpenAIEmbeddingProvider>>();

        var provider = new OpenAIEmbeddingProvider(mockConfig.Object, mockLogger.Object);
        var texts = new List<string> { "Test 1", "Test 2" };

        // Act
        var result = await provider.GenerateEmbeddingsAsync(texts);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().HaveCount(1536);
        result[1].Should().HaveCount(1536);
        result[0][0].Should().Be(0f); // Default float value
    }
}
