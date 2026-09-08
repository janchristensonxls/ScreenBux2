namespace ScreenBux.Data.Entities;

/// <summary>
/// Regex-based match assigning a process/window to an <see cref="AppCategory"/>, mirroring the
/// matching shape of <c>PolicyRule</c> in ScreenBux.Shared. Evaluated in order; the first rule
/// that matches wins. Processes matching no rule fall back to the account's "Other" category.
/// </summary>
public class AppCategoryRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AppCategoryId { get; set; }

    public AppCategory? AppCategory { get; set; }

    public string? ProcessNameRegex { get; set; }

    public string? WindowTitleRegex { get; set; }

    public bool Enabled { get; set; } = true;
}
