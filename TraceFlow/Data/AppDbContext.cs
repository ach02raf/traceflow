using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TraceFlow.Domain;

namespace TraceFlow.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) { }
    public DbSet<ProductionUnit> Units => Set<ProductionUnit>();
    public DbSet<UnitEvent> Events => Set<UnitEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ProductionUnit>().HasIndex(u => u.SerialNumber).IsUnique();
        b.Entity<ProductionUnit>().Property(u => u.Station).HasConversion<string>();
    }
}
