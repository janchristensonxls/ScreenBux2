using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data.Entities;

namespace ScreenBux.Data;

/// <summary>
/// EF Core context backing accounts (ASP.NET Core Identity), child profiles, devices,
/// device link codes, and policy documents.
/// </summary>
public class AppDbContext : IdentityDbContext<Account>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ChildProfile> ChildProfiles => Set<ChildProfile>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceLinkCode> DeviceLinkCodes => Set<DeviceLinkCode>();

    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ChildProfile>(entity =>
        {
            entity.HasOne(c => c.Account)
                .WithMany(a => a.ChildProfiles)
                .HasForeignKey(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Device>(entity =>
        {
            entity.HasIndex(d => d.MachineKey).IsUnique();

            entity.HasOne(d => d.Account)
                .WithMany(a => a.Devices)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.ChildProfile)
                .WithMany(c => c.Devices)
                .HasForeignKey(d => d.ChildProfileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<DeviceLinkCode>(entity =>
        {
            entity.HasIndex(l => l.Code).IsUnique();
        });

        builder.Entity<PolicyDocument>(entity =>
        {
            entity.HasOne(p => p.Account)
                .WithMany(a => a.PolicyDocuments)
                .HasForeignKey(p => p.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
