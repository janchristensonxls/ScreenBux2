using System.Text.RegularExpressions;

namespace ScreenBux.Shared.Models;

/// <summary>
/// A single regex match rule assigning a process/window to an <see cref="AppCategoryConfig"/>.
/// Mirrors the matching shape of the retired <c>PolicyRule.ProcessMatch</c> condition, and the
/// EF <c>AppCategoryRule</c> entity in ScreenBux.Data - this is the synced, client-side copy
/// the Service uses to classify processes without a network round-trip.
/// </summary>
public class AppCategoryRuleConfig
{
    public string? ProcessNameRegex { get; set; }

    public string? WindowTitleRegex { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True if this rule matches the given process name and/or window title.
    /// <paramref name="windowTitle"/> is only consulted when <paramref name="isForegroundWindow"/>
    /// is true, since window titles are only known for the foreground process (see
    /// docs/decisions/group-based-policy-model.md).
    /// </summary>
    public bool Matches(string processName, string? windowTitle, bool isForegroundWindow)
    {
        if (!Enabled)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(ProcessNameRegex) && Regex.IsMatch(processName, ProcessNameRegex, RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (isForegroundWindow && !string.IsNullOrEmpty(WindowTitleRegex) && !string.IsNullOrEmpty(windowTitle)
            && Regex.IsMatch(windowTitle, WindowTitleRegex, RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// A named group of applications (e.g. "Games", "Social Media", "Other"), embedded in the
/// synced <see cref="PolicyConfiguration"/> so the Service can classify processes locally.
/// Identified by <see cref="Name"/> - see docs/decisions/group-based-policy-model.md for why
/// category identity flows by name rather than a synced database id.
/// </summary>
public class AppCategoryConfig
{
    public string Name { get; set; } = string.Empty;

    public List<AppCategoryRuleConfig> Rules { get; set; } = new();
}
