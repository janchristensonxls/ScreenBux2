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

    public DbSet<PolicyProfile> PolicyProfiles => Set<PolicyProfile>();

    public DbSet<DeviceGrant> DeviceGrants => Set<DeviceGrant>();

    public DbSet<AppCategory> AppCategories => Set<AppCategory>();

    public DbSet<AppCategoryRule> AppCategoryRules => Set<AppCategoryRule>();

    public DbSet<UsageDailyTotal> UsageDailyTotals => Set<UsageDailyTotal>();

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

            entity.HasOne(p => p.ActivePolicyProfile)
                .WithMany()
                .HasForeignKey(p => p.ActivePolicyProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PolicyProfile>(entity =>
        {
            entity.HasOne(p => p.Account)
                .WithMany(a => a.PolicyProfiles)
                .HasForeignKey(p => p.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.ChildProfile)
                .WithMany()
                .HasForeignKey(p => p.ChildProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DeviceGrant>(entity =>
        {
            entity.HasIndex(g => g.DeviceId).IsUnique();

            entity.HasOne(g => g.Device)
                .WithOne()
                .HasForeignKey<DeviceGrant>(g => g.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppCategory>(entity =>
        {
            entity.HasOne(c => c.Account)
                .WithMany()
                .HasForeignKey(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppCategoryRule>(entity =>
        {
            entity.HasOne(r => r.AppCategory)
                .WithMany(c => c.Rules)
                .HasForeignKey(r => r.AppCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsageDailyTotal>(entity =>
        {
            entity.HasIndex(u => new { u.EffectiveDate, u.ChildProfileId, u.DeviceId, u.AppCategoryId }).IsUnique();

            entity.HasOne(u => u.ChildProfile)
                .WithMany()
                .HasForeignKey(u => u.ChildProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(u => u.Device)
                .WithMany()
                .HasForeignKey(u => u.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(u => u.AppCategory)
                .WithMany()
                .HasForeignKey(u => u.AppCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
