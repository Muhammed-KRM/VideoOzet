namespace VideoOzet.Business.Events;

public class TokenPurchasedEvent
{
    public Guid UserId { get; set; }
    public int Amount { get; set; }
}

public class TokenSpentEvent
{
    public Guid UserId { get; set; }
    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
