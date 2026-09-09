using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TokensController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public TokensController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [HttpGet("balance")]
    public async Task<ActionResult<int>> GetBalance()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _tokenService.GetBalanceAsync(userId));
    }

    [HttpGet("packages")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TokenPackageDto>>> GetPackages()
        => Ok(await _tokenService.GetPackagesAsync());

    [HttpGet("history")]
    public async Task<ActionResult<List<TokenTransactionDto>>> GetHistory()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _tokenService.GetTransactionHistoryAsync(userId));
    }

    public class PurchaseRequestDto 
    {
        public int PackageId { get; set; }
    }

    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] PurchaseRequestDto req, [FromServices] IPaymentServiceFactory paymentFactory)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var packages = await _tokenService.GetPackagesAsync();
        var pkg = packages.FirstOrDefault(p => p.Id == req.PackageId);
        
        if (pkg == null) return BadRequest("Geçersiz paket");

        var paymentService = paymentFactory.GetPaymentService("TR");

        var result = await paymentService.ProcessPaymentAsync(new PaymentRequest
        {
            UserId = userId,
            Amount = pkg.Price,
            Description = $"{pkg.TokenCount} Jeton Paketi Alımı",
            ReturnUrl = $"{Request.Scheme}://{Request.Host}/api/tokens/payment-callback?packageId={pkg.Id}&userId={userId}"
        });

        if (!result.Success)
            return BadRequest(result.ErrorMessage ?? "Ödeme başlatılamadı");

        return Ok(new { paymentUrl = result.RedirectUrl ?? "#" });
    }

    [HttpPost("payment-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentCallback(
        [FromQuery] int packageId,
        [FromQuery] Guid userId,
        [FromServices] IPaymentServiceFactory paymentFactory,
        [FromServices] MassTransit.IPublishEndpoint publishEndpoint)
    {
        // Callback parametrelerini lügat (Dictionary) yapısına çeviriyoruz
        var callbackData = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());
        
        var paymentService = paymentFactory.GetPaymentService("TR");
        var isValid = await paymentService.VerifyCallbackAsync(callbackData);
        
        if (!isValid) return BadRequest("Payment verification failed");

        // Idempotency: Aynı token (veya TransactionId) ile tekrar gelen callback'i işleme
        // Not: Gerçek senaryoda bu TransactionId üzerinden kontrol edilmeli
        var history = await _tokenService.GetTransactionHistoryAsync(userId);
        if (history.Any(t => t.Description.Contains("Payment Successful") && t.CreatedAt > DateTime.UtcNow.AddMinutes(-5)))
            return Ok(new { message = "Bu ödeme zaten işlendi." });

        var packages = await _tokenService.GetPackagesAsync();
        var pkg = packages.FirstOrDefault(p => p.Id == packageId);
        if (pkg == null) return BadRequest("Invalid package in callback");

        await _tokenService.AddTokenAsync(userId, pkg.TokenCount, $"Kredi Kartı ile Alım (Paket: {pkg.Name})");

        await publishEndpoint.Publish(new VideoOzet.Business.Events.TokenPurchasedEvent
        {
            UserId = userId,
            Amount = pkg.TokenCount
        });

        return Redirect("/panel/jetonlarim?status=success");
    }
}
