using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TraceFlow.Services;
namespace TraceFlow.Hubs;

[Authorize] // only logged-in users can connect (auth cookie is sent with the handshake)
public class TraceHub : Hub
{
    private readonly PresenceTracker _presence;
    private readonly GuestService _guests;
    public TraceHub(PresenceTracker p, GuestService g) { _presence = p; _guests = g; }

    public override async Task OnConnectedAsync()
    {
        var name = Context.User!.Identity!.Name!;
        var first = _presence.Add(name, Context.ConnectionId);

        await Clients.All.SendAsync("PresenceChanged", _presence.Online());
        // Tell everybody else that someone just logged in (not for a 2nd tab of the same user)
        if (first) await Clients.Others.SendAsync("UserJoined", name, GuestService.IsGuest(name));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        var name = Context.User!.Identity!.Name!;
        var last = _presence.Remove(name, Context.ConnectionId);

        await Clients.All.SendAsync("PresenceChanged", _presence.Online());
        if (last) await Clients.Others.SendAsync("UserLeft", name);

        // Guest left the app: delete the account after a 20 s grace period (refresh-safe)
        if (GuestService.IsGuest(name)) _guests.ScheduleDelete(name, TimeSpan.FromSeconds(20));

        await base.OnDisconnectedAsync(ex);
    }
}
