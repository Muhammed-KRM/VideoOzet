using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VideoOzet.API.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        await base.OnConnectedAsync();
    }
}
