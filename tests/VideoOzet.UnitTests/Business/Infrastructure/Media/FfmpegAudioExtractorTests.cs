using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VideoOzet.Business.Infrastructure.Media;

namespace VideoOzet.UnitTests.Business.Infrastructure.Media;

public class FfmpegAudioExtractorTests
{
    private readonly Mock<ILogger<FfmpegAudioExtractor>> _mockLogger;

    public FfmpegAudioExtractorTests()
    {
        _mockLogger = new Mock<ILogger<FfmpegAudioExtractor>>();
    }

    [Fact]
    public async Task ExtractAudioAsync_ShouldReturnFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var extractor = new FfmpegAudioExtractor(_mockLogger.Object);

        // Act
        // Because the input file doesn't exist, FFmpeg process should fail and throw/catch exception.
        var result = await extractor.ExtractAudioAsync("non_existent_file.mp4", "output.mp3");

        // Assert
        result.Should().BeFalse();
    }
}
