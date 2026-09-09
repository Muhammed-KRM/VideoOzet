using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
        => _notificationService = notificationService;

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _notificationService.GetUserNotificationsAsync(GetUserId(), page, pageSize));

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
        => Ok(new { count = await _notificationService.GetUnreadCountAsync(GetUserId()) });

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        await _notificationService.MarkAsReadAsync(id, GetUserId());
        return Ok();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(GetUserId());
        return Ok();
    }
}
