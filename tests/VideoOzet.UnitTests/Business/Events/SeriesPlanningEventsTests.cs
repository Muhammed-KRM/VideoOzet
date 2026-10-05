using System;
using FluentAssertions;
using VideoOzet.Business.Events;
using Xunit;

namespace VideoOzet.UnitTests.Business.Events;

public class SeriesPlanningEventsTests
{
    [Fact]
    public void TopicAnalysisRequestedEvent_ShouldInitializeCorrectly()
    {
        var egitimId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var evt = new TopicAnalysisRequestedEvent
        {
            EgitimId = egitimId,
            ContentRequestId = requestId
        };

        evt.EgitimId.Should().Be(egitimId);
        evt.ContentRequestId.Should().Be(requestId);
    }
    
    [Fact]
    public void SeriesVideoGenerationCommand_ShouldInitializeCorrectly()
    {
        var egitimId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var bolumId = Guid.NewGuid();
        
        var cmd = new SeriesVideoGenerationCommand
        {
            EgitimId = egitimId,
            ContentRequestId = requestId,
            SeriPlaniId = planId,
            SeriBolumId = bolumId,
            BolumNo = 2
        };
        
        cmd.EgitimId.Should().Be(egitimId);
        cmd.SeriBolumId.Should().Be(bolumId);
        cmd.BolumNo.Should().Be(2);
    }
}
