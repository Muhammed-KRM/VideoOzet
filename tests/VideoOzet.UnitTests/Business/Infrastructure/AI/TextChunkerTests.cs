using FluentAssertions;
using System.Collections.Generic;
using VideoOzet.Business.Infrastructure.AI;
using Xunit;

namespace VideoOzet.UnitTests.Business.Infrastructure.AI;

public class TextChunkerTests
{
    private readonly TextChunker _chunker;

    public TextChunkerTests()
    {
        _chunker = new TextChunker();
    }

    [Fact]
    public void ChunkText_ShouldReturnEmptyList_WhenTextIsNullOrWhitespace()
    {
        // Act
        var resultNull = _chunker.ChunkText(null);
        var resultEmpty = _chunker.ChunkText("   ");

        // Assert
        resultNull.Should().BeEmpty();
        resultEmpty.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_ShouldSplitText_WhenTextExceedsMaxTokens()
    {
        // Arrange
        // Create a string with exactly 10 words.
        var text = "bir iki uc dort bes alti yedi sekiz dokuz on";
        var maxTokens = 5;

        // Act
        var result = _chunker.ChunkText(text, maxTokens);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be("bir iki uc dort bes");
        result[1].Should().Be("alti yedi sekiz dokuz on");
    }

    [Fact]
    public void ChunkText_ShouldNotSplitText_WhenTextIsUnderMaxTokens()
    {
        // Arrange
        var text = "bir iki uc";
        var maxTokens = 5;

        // Act
        var result = _chunker.ChunkText(text, maxTokens);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("bir iki uc");
    }
}
