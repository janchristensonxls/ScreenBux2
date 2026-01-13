namespace ScreenBux.Shared.Models;

/// <summary>
/// Represents a rule for closing processes based on regex matching.
/// </summary>
public class PolicyRule
{
    public string Name { get; set; } = string.Empty;
    public string? ProcessNameRegex { get; set; }
    public string? WindowTitleRegex { get; set; }
    public bool Enabled { get; set; } = true;
}
