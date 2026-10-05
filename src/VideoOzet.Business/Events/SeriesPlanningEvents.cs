using System;

namespace VideoOzet.Business.Events;

public class TopicAnalysisRequestedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
}

public class TopicAnalysisCompletedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid KonuAnaliziId { get; set; }
}

public class SeriesPlanRequestedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
}

public class SeriesPlanGeneratedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriPlaniId { get; set; }
}

public class SeriesPlanApprovedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriPlaniId { get; set; }
}

public class SeriesVideoGenerationCommand
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriPlaniId { get; set; }
    public Guid SeriBolumId { get; set; }
    public int BolumNo { get; set; }
}

public class SeriesVideoRevisionRequestedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriBolumId { get; set; }
    public string Talimat { get; set; } = string.Empty;
    public string HedefAlan { get; set; } = "Hepsi";
}

public class SeriesVideoGeneratedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriBolumId { get; set; }
    public Guid BolumRevizyonuId { get; set; }
}

public class SeriesPlanCompletedEvent
{
    public Guid ContentRequestId { get; set; }
    public Guid EgitimId { get; set; }
    public Guid SeriPlaniId { get; set; }
}

