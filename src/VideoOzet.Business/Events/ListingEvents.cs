namespace VideoOzet.Business.Events;

public class ListingCreatedEvent
{
    public Guid ListingId { get; set; }
}

public class ListingUpdatedEvent
{
    public Guid ListingId { get; set; }
}

public class ListingDeletedEvent
{
    public Guid ListingId { get; set; }
}
