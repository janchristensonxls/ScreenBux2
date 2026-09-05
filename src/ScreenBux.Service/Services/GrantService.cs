using System.Text.Json;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service-side cache of the current device time grant. Persists <see cref="ExpiresAtUtc"/>
/// to a local grant.json so enforcement keeps honoring an active grant across restarts and
/// while disconnected from the WebServer - "is a grant active" is always a local, stateless
/// comparison against <see cref="DateTime.UtcNow"/>, never dependent on live connectivity.
/// </summary>
public class GrantService
{
    private readonly ILogger<GrantService> _logger;
    private readonly string _grantFilePath;
    private DateTime? _expiresAtUtc;

    public GrantService(ILogger<GrantService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _grantFilePath = configuration["GrantFilePath"] ?? PolicyStorage.GetDefaultGrantPath();
        PolicyStorage.EnsurePolicyDirectory(_grantFilePath);
    }

    /// <summary>The current grant expiry, or null if there is no grant on disk.</summary>
    public DateTime? ExpiresAtUtc => _expiresAtUtc;

    /// <summary>True while a grant is active, i.e. all enforcement should be paused.</summary>
    public bool IsGrantActive => _expiresAtUtc is DateTime expires && expires > DateTime.UtcNow;

    public async Task LoadGrantAsync()
    {
        try
        {
            if (!File.Exists(_grantFilePath))
            {
                _expiresAtUtc = null;
                return;
            }

            var json = await File.ReadAllTextAsync(_grantFilePath);
            var state = JsonSerializer.Deserialize<GrantState>(json);
            _expiresAtUtc = state?.ExpiresAtUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading grant file");
            _expiresAtUtc = null;
        }
    }

    public async Task UpdateGrantAsync(DateTime? expiresAtUtc)
    {
        _expiresAtUtc = expiresAtUtc;

        try
        {
            var json = JsonSerializer.Serialize(new GrantState { ExpiresAtUtc = expiresAtUtc }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_grantFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving grant file");
        }
    }

    private class GrantState
    {
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
