using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TraceFlow.Data;
using TraceFlow.Hubs;
using TraceFlow.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Connection string: Render gives DATABASE_URL (postgres://user:pass@host/db) ----
var cs = builder.Configuration.GetConnectionString("Default");
var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(dbUrl))
{
    var u = new Uri(dbUrl);
    var ui = u.UserInfo.Split(':', 2);
    cs = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = u.Host,
        Port = u.Port > 0 ? u.Port : 5432,
        Username = Uri.UnescapeDataString(ui[0]),
        Password = Uri.UnescapeDataString(ui[1]),
        Database = u.AbsolutePath.TrimStart('/')
    }.ConnectionString;
}
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));

builder.Services.AddDefaultIdentity<IdentityUser>(o =>
    {
        o.Password.RequireNonAlphanumeric = false;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Re-validate the auth cookie often, so a deleted guest is kicked out within 30 s
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromSeconds(30));

// Render terminates HTTPS in front of the app: trust X-Forwarded-* headers
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddResponseCompression();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<GuestService>();
builder.Services.AddHostedService<GuestCleanupService>();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // safe because views use asp-append-version (content hash in the URL)
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=604800"
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapHub<TraceHub>("/hubs/trace");

// Create / migrate DB, retry while Postgres is starting
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var i = 0; i < 15; i++)
    {
        try
        {
            if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
            else await db.Database.EnsureCreatedAsync();
            break;
        }
        catch when (i < 14) { await Task.Delay(2000); }
    }
}
await SeedData.InitAsync(app.Services);

app.Run();
