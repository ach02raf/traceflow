namespace TraceFlow.Services;

public class GuestCleanupService : BackgroundService
{
    private readonly GuestService _guests;
    private readonly ILogger<GuestCleanupService> _log;
    public GuestCleanupService(GuestService g, ILogger<GuestCleanupService> l) { _guests = g; _log = l; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // On startup nobody is connected: remove every leftover guest.
        try { await _guests.SweepAsync(TimeSpan.Zero); } catch (Exception e) { _log.LogWarning(e, "Startup sweep failed"); }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await _guests.SweepAsync(TimeSpan.FromMinutes(2)); }
            catch (Exception e) { _log.LogWarning(e, "Guest sweep failed"); }
        }
    }
}
