namespace VideoOzet.Business.Exceptions;

public class InsufficientTokenException : BusinessException
{
    public int CurrentBalance { get; }
    public int RequiredAmount { get; }

    public InsufficientTokenException(int currentBalance, int requiredAmount)
        : base($"Yetersiz jeton. Mevcut bakiye: {currentBalance}, gereken: {requiredAmount}")
    {
        CurrentBalance = currentBalance;
        RequiredAmount = requiredAmount;
    }
}
