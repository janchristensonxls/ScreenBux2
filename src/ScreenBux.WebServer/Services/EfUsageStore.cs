using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// EF Core-backed <see cref="IUsageStore"/>. One <see cref="UsageDailyTotal"/> row per
/// (effective day, child, device, category), created lazily and updated via delta increments -
/// never one row per usage tick. See docs/decisions/screen-time-usage-tracking.md.
/// </summary>
public class EfUsageStore : IUsageStore
{
    private const string DefaultCategoryName = "Other";

    private readonly AppDbContext _db;
    private readonly ILogger<EfUsageStore> _logger;

    public EfUsageStore(AppDbContext db, ILogger<EfUsageStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UsageSummaryDto> AddUsageSecondsAsync(string accountId, AddUsageSecondsRequest request, CancellationToken cancellationToken = default)
    {
        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.AccountId == accountId, cancellationToken);

        if (device is null)
        {
            throw new InvalidOperationException($"Device {request.DeviceId} was not found for account {accountId}.");
        }

        if (device.ChildProfileId is not Guid childProfileId)
        {
            throw new InvalidOperationException($"Device {request.DeviceId} is not assigned to a child profile.");
        }

        var category = await GetOrCreateCategoryAsync(accountId, request.CategoryName, cancellationToken);

        var total = await _db.UsageDailyTotals.FirstOrDefaultAsync(
            u => u.EffectiveDate == request.EffectiveDate
                && u.ChildProfileId == childProfileId
                && u.DeviceId == request.DeviceId
                && u.AppCategoryId == category.Id,
            cancellationToken);

        if (total is null)
        {
            total = new UsageDailyTotal
            {
                EffectiveDate = request.EffectiveDate,
                ChildProfileId = childProfileId,
                DeviceId = request.DeviceId,
                AppCategoryId = category.Id,
                Seconds = 0
            };
            _db.UsageDailyTotals.Add(total);
        }

        total.Seconds = Math.Max(0, total.Seconds + request.Seconds);
        total.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Added {Seconds}s usage for device {DeviceId} category {CategoryName} on {EffectiveDate}",
            request.Seconds, request.DeviceId, category.Name, request.EffectiveDate);

        return await GetSummaryAsync(accountId, childProfileId, request.EffectiveDate, cancellationToken);
    }

    public async Task<UsageSummaryDto> GetSummaryAsync(string accountId, Guid childProfileId, DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        var childExists = await _db.ChildProfiles.AnyAsync(c => c.Id == childProfileId && c.AccountId == accountId, cancellationToken);
        if (!childExists)
        {
            throw new InvalidOperationException($"Child profile {childProfileId} was not found for account {accountId}.");
        }

        var totals = await _db.UsageDailyTotals
            .Where(u => u.ChildProfileId == childProfileId && u.EffectiveDate == effectiveDate)
            .Include(u => u.AppCategory)
            .ToListAsync(cancellationToken);

        return new UsageSummaryDto
        {
            ChildProfileId = childProfileId,
            EffectiveDate = effectiveDate,
            TotalSeconds = totals.Sum(t => t.Seconds),
            ByCategory = totals
                .GroupBy(t => t.AppCategoryId)
                .Select(g => new UsageCategoryTotalDto
                {
                    AppCategoryId = g.Key,
                    AppCategoryName = g.First().AppCategory?.Name ?? DefaultCategoryName,
                    Seconds = g.Sum(t => t.Seconds)
                })
                .ToList(),
            ByDevice = totals
                .GroupBy(t => t.DeviceId)
                .Select(g => new UsageDeviceTotalDto
                {
                    DeviceId = g.Key,
                    Seconds = g.Sum(t => t.Seconds)
                })
                .ToList()
        };
    }

    private async Task<AppCategory> GetOrCreateCategoryAsync(string accountId, string? categoryName, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(categoryName) ? DefaultCategoryName : categoryName.Trim();

        var category = await _db.AppCategories
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Name == name, cancellationToken);

        if (category is not null)
        {
            return category;
        }

        category = new AppCategory
        {
            AccountId = accountId,
            Name = name,
            IsSystemDefault = name == DefaultCategoryName
        };
        _db.AppCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return category;
    }
}
