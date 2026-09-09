using System;
using FluentAssertions;
using VideoOzet.Business.Exceptions;
using Xunit;

namespace VideoOzet.UnitTests.Business.Exceptions;

public class BusinessExceptionsTests
{
    [Fact]
    public void BusinessException_ShouldSetMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BusinessException("business error", inner);

        ex.Message.Should().Be("business error");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void NotFoundException_ShouldFormatMessageCorrectly()
    {
        var ex = new NotFoundException("Eğitim", "123");
        ex.Message.Should().Contain("Eğitim");
        ex.Message.Should().Contain("123");
    }

    [Fact]
    public void InsufficientTokenException_ShouldSetBalanceAndAmount()
    {
        var ex = new InsufficientTokenException(10, 50);

        ex.CurrentBalance.Should().Be(10);
        ex.RequiredAmount.Should().Be(50);
        ex.Message.Should().Contain("10");
        ex.Message.Should().Contain("50");
    }

    [Fact]
    public void UnauthorizedException_ShouldUseDefaultOrCustomMessage()
    {
        var defaultEx = new UnauthorizedException();
        defaultEx.Message.Should().NotBeNullOrWhiteSpace();

        var customEx = new UnauthorizedException("Özel yetki hatası");
        customEx.Message.Should().Be("Özel yetki hatası");
    }

    [Fact]
    public void PipelineException_ShouldSetMessageAndInnerException()
    {
        var inner = new Exception("inner");
        var ex = new PipelineException("pipeline error", inner);

        ex.Message.Should().Be("pipeline error");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void PipelineSubExceptions_ShouldInstantiateCorrectly()
    {
        var inner = new Exception("inner");

        var sttEx = new SttException("stt error", inner);
        sttEx.Message.Should().Be("stt error");
        sttEx.InnerException.Should().Be(inner);

        var contentEx = new ContentGenerationException("content error", inner);
        contentEx.Message.Should().Be("content error");
        contentEx.InnerException.Should().Be(inner);

        var embeddingEx = new EmbeddingException("embedding error", inner);
        embeddingEx.Message.Should().Be("embedding error");
        embeddingEx.InnerException.Should().Be(inner);
    }
}
