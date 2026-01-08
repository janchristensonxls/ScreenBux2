namespace ScreenBux.Shared.Models;

/// <summary>
/// Root configuration for parental control policies
/// </summary>
public class PolicyConfiguration
{
    public List<AppPolicy> Policies { get; set; } = new();
    public bool EnableMonitoring { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 5;
    public bool LogActivity { get; set; } = true;
}
