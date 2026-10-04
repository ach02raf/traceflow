# TraceFlow

Real-time production traceability board. Operators log in, register serial numbers and move them through
**Assembly → Test → Packing → Shipped**. Every connected user sees changes instantly; supervisors can flag defects.

**Stack:** ASP.NET Core 8 MVC · SignalR · KnockoutJS (MVVM) · EF Core · PostgreSQL · ASP.NET Identity (roles) · xUnit · Docker · GitHub Actions

## Run it (option A: everything in Docker)
```bash
docker compose up --build
# open http://localhost:8080
```

## Run it (option B: dev mode in VS Code)
```bash
docker compose up -d db
dotnet run --project TraceFlow      # http://localhost:5080
dotnet test
```

## Deploy on Render (free)
1. Push this folder to a GitHub repository (the `render.yaml` must be at the repo root).
2. render.com > **New > Blueprint** > select the repo > **Apply**.
3. Wait for the build (~5 min), then open `https://traceflow-xxxx.onrender.com`.

Notes: the free web service sleeps after 15 min without traffic (about 1 min to wake up).
The free Postgres expires after 30 days (+14 days grace), so recreate it or upgrade.
The app reads `DATABASE_URL` automatically and creates tables + demo accounts on first start.

## Demo accounts (password `Demo123!`)
| Email | Role |
|---|---|
| operator@demo.com | Operator |
| supervisor@demo.com | Supervisor (can flag defects) |

Open two browsers (one per account) to see real-time sync and presence.

## Guest mode
"Continue as guest" on the landing page creates a temporary `guest-xxxxxx` account (Operator role, session cookie).
It is deleted automatically: 20 s after the last SignalR connection closes (refresh-safe), immediately on **Leave**,
and by a background sweeper (`GuestCleanupService`) that also cleans leftovers after a server restart.
History entries keep the guest name, so traceability is preserved.

## Front-end optimisations
- Knockout `deferUpdates`, single-pass group/filter/sort, `Map` index, one shared timer, rate-limited search
- Skeleton loader, toast cap, no toast for your own actions, live stats, `/` shortcut, reduced-motion support
- Server: response compression, long cache headers on versioned static files, preconnect to CDNs

## Notifications and responsive UI
- When another user logs in, every other connected user gets a toast and an entry in the **Activity** panel (bell icon with unread badge). Second tabs and page refreshes do not trigger it.
- Mobile: one station at a time through a tab bar, full-width toasts, fullscreen history modal, large touch targets, safe-area support.
- Perf: delegated click listener, `content-visibility`, deferred scripts, batched Knockout updates.

## Architecture
- `Hubs/TraceHub`: authenticated SignalR hub + `PresenceTracker` (online users, multi-tab safe)
- `Controllers/UnitsController`: REST API; persists via EF Core, then pushes updates with `IHubContext`
- `wwwroot/js/dashboard.js`: Knockout view-models (observables, pureComputed, custom `flash` binding)
- Security: cookie auth shared with SignalR, server-side role authorization, CSRF header on API calls
- Scaling idea: Redis backplane for SignalR across several servers
