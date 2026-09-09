using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessagesController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpGet("inbox")]
    public async Task<ActionResult<List<MessageDto>>> GetInbox()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _messageService.GetInboxAsync(userId));
    }

    [HttpGet("conversation/{otherUserId}")]
    public async Task<ActionResult<List<MessageDto>>> GetConversation(Guid otherUserId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _messageService.GetConversationAsync(userId, otherUserId));
    }

    [HttpPost]
    public async Task<ActionResult<MessageDto>> Send(MessageSendDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _messageService.SendMessageAsync(dto, userId));
    }

    [HttpPost("{messageId}/unlock")]
    public async Task<IActionResult> Unlock(Guid messageId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _messageService.UnlockMessageAsync(messageId, userId);
        return Ok();
    }
}
