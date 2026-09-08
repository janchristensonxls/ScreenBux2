namespace ScreenBux.Data.Entities;

/// <summary>
/// A named group of applications (e.g. "Games", "Social Media", "Other") that a per-day usage
/// budget can be attached to independently of the "total for the day" budget. One row per
/// account, plus a seeded account-wide "Other" fallback category for processes that don't match
/// any <see cref="AppCategoryRule"/>.
/// </summary>
public class AppCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True for the built-in "Other" category seeded per account; cannot be deleted/renamed by
    /// the parent since it's the fallback bucket for unmatched processes.
    /// </summary>
    public bool IsSystemDefault { get; set; }

    public ICollection<AppCategoryRule> Rules { get; set; } = new List<AppCategoryRule>();
}
