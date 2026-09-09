using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var profile = await _userService.GetProfileAsync(userId);
        return Ok(profile);
    }

    [HttpPut("personal-info")]
    public async Task<IActionResult> UpdatePersonalInfo([FromBody] PersonalInfoDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.UpdatePersonalInfoAsync(userId, dto);
        return NoContent();
    }

    [HttpPut("payment-info")]
    public async Task<IActionResult> UpdatePaymentInfo([FromBody] PaymentInfoDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.UpdatePaymentInfoAsync(userId, dto);
        return NoContent();
    }

    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.ChangePasswordAsync(userId, dto);
        return NoContent();
    }

    [HttpPut("notification-settings")]
    public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _userService.UpdateNotificationSettingsAsync(userId, dto);
        return NoContent();
    }
}
