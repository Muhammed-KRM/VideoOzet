namespace VideoOzet.Business.Interfaces;

/// <summary>
/// Strategy Pattern: Ödeme sağlayıcılarının ortak arayüzü.
/// PayTR, Iyzico, Stripe gibi farklı sağlayıcılar bu arayüzü implemente eder.
/// </summary>
public interface IPaymentService
{
    /// <summary>Sağlayıcı adı (PayTR, Iyzico, Stripe vb.)</summary>
    string ProviderName { get; }

    /// <summary>Ödeme başlatır ve yönlendirme URL'i döndürür.</summary>
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);

    /// <summary>Ödeme callback'ini (webhook) doğrular.</summary>
    Task<bool> VerifyCallbackAsync(Dictionary<string, string> callbackData);

    /// <summary>İade işlemi yapar.</summary>
    Task<bool> RefundAsync(string transactionId, decimal amount);
}

/// <summary>
/// Factory Pattern: Ülke koduna göre doğru ödeme sağlayıcısını seçer.
/// </summary>
public interface IPaymentServiceFactory
{
    IPaymentService GetPaymentService(string countryCode = "TR");
}

// ─── DTO'lar ─────────────────────────────────────────────────

public class PaymentRequest
{
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Description { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string? BuyerEmail { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerIp { get; set; }
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? RedirectUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
