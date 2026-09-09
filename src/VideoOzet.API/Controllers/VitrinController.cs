using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VitrinController : ControllerBase
{
    private readonly IVitrinService _vitrinService;

    public VitrinController(IVitrinService vitrinService)
    {
        _vitrinService = vitrinService;
    }

    [HttpGet("packages")]
    [AllowAnonymous]
    public async Task<ActionResult<List<VitrinPackageDto>>> GetPackages()
        => Ok(await _vitrinService.GetPackagesAsync());

    public class VitrinPurchaseRequestDto
    {
        public int PackageId { get; set; }
    }

    [HttpPost("{listingId}/purchase")]
    public async Task<IActionResult> Purchase(Guid listingId, [FromBody] VitrinPurchaseRequestDto req, [FromServices] IPaymentServiceFactory paymentFactory)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var packages = await _vitrinService.GetPackagesAsync();
        var pkg = packages.FirstOrDefault(p => p.Id == req.PackageId);
        
        if (pkg == null) return BadRequest("Geçersiz paket");

        // Factory'den uygun servis alınır (Varsayılan TR)
        var paymentService = paymentFactory.GetPaymentService("TR");

        var result = await paymentService.ProcessPaymentAsync(new PaymentRequest
        {
            UserId = userId,
            Amount = pkg.Price,
            Description = $"Vitrin Paketi: {pkg.Name} - İlan ID: {listingId}",
            ReturnUrl = $"{Request.Scheme}://{Request.Host}/api/vitrin/payment-callback?listingId={listingId}&packageId={pkg.Id}&userId={userId}"
        });

        if (!result.Success)
            return BadRequest(result.ErrorMessage ?? "Ödeme başlatılamadı");

        return Ok(new { paymentUrl = result.RedirectUrl ?? "#" });
    }

    [HttpPost("payment-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentCallback(
        [FromQuery] int packageId,
        [FromQuery] Guid listingId,
        [FromQuery] Guid userId,
        [FromServices] IPaymentServiceFactory paymentFactory)
    {
        // Callback parametrelerini lügat (Dictionary) yapısına çeviriyoruz
        var callbackData = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());
        
        var paymentService = paymentFactory.GetPaymentService("TR");
        var isValid = await paymentService.VerifyCallbackAsync(callbackData);
        
        if (!isValid) return BadRequest("Payment verification failed");

        await _vitrinService.PurchaseVitrinAsync(listingId, packageId, userId);

        return Redirect("/panel/ilanlarim?status=vitrin_success");
    }
}
