using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScreenBux.WebServer.Models;

namespace ScreenBux.WebServer.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Household> Households => Set<Household>();
    public DbSet<UserHousehold> UserHouseholds => Set<UserHousehold>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserHousehold>()
            .HasKey(uh => new { uh.UserId, uh.HouseholdId });

        builder.Entity<UserHousehold>()
            .HasOne(uh => uh.User)
            .WithMany(u => u.UserHouseholds)
            .HasForeignKey(uh => uh.UserId);

        builder.Entity<UserHousehold>()
            .HasOne(uh => uh.Household)
            .WithMany(h => h.UserHouseholds)
            .HasForeignKey(uh => uh.HouseholdId);

        builder.Entity<Device>()
            .HasKey(d => d.DeviceId);

        builder.Entity<Device>()
            .HasOne(d => d.Household)
            .WithMany(h => h.Devices)
            .HasForeignKey(d => d.HouseholdId);

        builder.Entity<PairingCode>()
            .HasOne(pc => pc.Household)
            .WithMany()
            .HasForeignKey(pc => pc.HouseholdId);
    }
}
