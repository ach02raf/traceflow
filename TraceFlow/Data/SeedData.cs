using Microsoft.AspNetCore.Identity;
using TraceFlow.Domain;

namespace TraceFlow.Data;

public static class SeedData
{
    public static async Task InitAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var r in new[] { "Operator", "Supervisor" })
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        async Task Make(string email, string role)
        {
            if (await users.FindByEmailAsync(email) != null) return;
            var u = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            await users.CreateAsync(u, "Demo123!");
            await users.AddToRoleAsync(u, role);
        }
        await Make("operator@demo.com", "Operator");
        await Make("supervisor@demo.com", "Supervisor");

        if (!db.Units.Any())
        {
            var s = new[] { ("SN-10001", Station.Assembly), ("SN-10002", Station.Test), ("SN-10003", Station.Packing) };
            foreach (var (serial, st) in s)
            {
                var u = new ProductionUnit { SerialNumber = serial, Station = st, UpdatedBy = "system" };
                u.Events.Add(new UnitEvent { Action = "Created (seed)", User = "system" });
                db.Units.Add(u);
            }
            await db.SaveChangesAsync();
        }
    }
}
