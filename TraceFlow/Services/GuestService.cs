using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TraceFlow.Hubs;

namespace TraceFlow.Services;

/// <summary>Creates temporary guest accounts and removes them when the person leaves.</summary>
public class GuestService
{
    public const string Prefix = "guest-";
    private const string CreatedClaim = "guest-created";
    private const int MaxGuests = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly PresenceTracker _presence;
    private readonly ILogger<GuestService> _log;

    public GuestService(IServiceScopeFactory scopes, PresenceTracker presence, ILogger<GuestService> log)
    { _scopes = scopes; _presence = presence; _log = log; }

    public static bool IsGuest(string? userName) =>
        userName != null && userName.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<IdentityUser?> CreateAsync()
    {
        using var scope = _scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        if (await users.Users.CountAsync(u => u.UserName!.StartsWith(Prefix)) >= MaxGuests) return null;

        var name = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        var user = new IdentityUser { UserName = name };
        var res = await users.CreateAsync(user);
        if (!res.Succeeded) return null;

        await users.AddToRoleAsync(user, "Operator");
        await users.AddClaimAsync(user, new Claim(CreatedClaim, DateTime.UtcNow.Ticks.ToString()));
        return user;
    }

    public async Task DeleteAsync(string userName)
    {
        if (!IsGuest(userName)) return;
        using var scope = _scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var u = await users.FindByNameAsync(userName);
        if (u != null) { await users.DeleteAsync(u); _log.LogInformation("Guest {Name} removed", userName); }
    }

    /// <summary>Delete a guest if it is still offline after a short grace period (page refresh safe).</summary>
    public void ScheduleDelete(string userName, TimeSpan grace)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(grace);
                if (!_presence.IsOnline(userName)) await DeleteAsync(userName);
            }
            catch (Exception e) { _log.LogWarning(e, "Guest cleanup failed"); }
        });
    }

    /// <summary>Safety net: removes offline guests older than minAge (covers crashes / restarts).</summary>
    public async Task SweepAsync(TimeSpan minAge)
    {
        using var scope = _scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var guests = await users.Users.Where(u => u.UserName!.StartsWith(Prefix)).ToListAsync();

        foreach (var g in guests)
        {
            if (_presence.IsOnline(g.UserName!)) continue;
            var claim = (await users.GetClaimsAsync(g)).FirstOrDefault(c => c.Type == CreatedClaim);
            var created = claim != null && long.TryParse(claim.Value, out var t)
                ? new DateTime(t, DateTimeKind.Utc) : DateTime.MinValue;
            if (DateTime.UtcNow - created >= minAge) await users.DeleteAsync(g);
        }
    }
}
