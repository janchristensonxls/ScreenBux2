namespace ScreenBux.Shared.Utilities;

public static class PolicyStorage
{
    public static string GetDefaultPolicyPath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "ScreenBux", "policy.json");
    }

    /// <summary>
    /// Returns the path to the device state file (device.json) stored next to policy.json.
    /// </summary>
    public static string GetDeviceStatePath()
    {
        var policyPath = GetDefaultPolicyPath();
        var directory = Path.GetDirectoryName(policyPath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, "device.json");
    }

    /// <summary>
    /// Returns true if this device has already been linked to a parent account,
    /// by checking whether device.json exists and contains a non-empty DeviceToken.
    /// </summary>
    public static bool IsDeviceLinked()
    {
        var path = GetDeviceStatePath();
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("DeviceToken", out var tokenEl))
            {
                var token = tokenEl.GetString();
                return !string.IsNullOrEmpty(token);
            }
        }
        catch { /* treat any read/parse error as not linked */ }

        return false;
    }

    public static void EnsurePolicyDirectory(string policyPath)
    {
        var directory = Path.GetDirectoryName(policyPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
