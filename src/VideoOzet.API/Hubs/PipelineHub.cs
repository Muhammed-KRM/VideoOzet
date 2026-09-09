using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace VideoOzet.API.Hubs;

[AllowAnonymous] // TODO: Admin Auth eklendiğinde [Authorize] yapılabilir
public class PipelineHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Gerekirse gruba ekleme yapılabilir
        await base.OnConnectedAsync();
    }
}
