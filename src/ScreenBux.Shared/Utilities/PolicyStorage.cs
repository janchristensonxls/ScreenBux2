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

    public static void EnsurePolicyDirectory(string policyPath)
    {
        var directory = Path.GetDirectoryName(policyPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
