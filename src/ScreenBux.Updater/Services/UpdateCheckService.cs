using System.Net.Http.Json;
using ScreenBux.Shared.Models.Updates;

namespace ScreenBux.Updater.Services;

/// <summary>
/// Periodically polls the WebServer's update manifest (<c>GET api/updates/latest</c>) and,
/// when a newer version is published for the Service or the Agent, downloads the update
/// package and applies it via <see cref="ServiceUpdater"/>/<see cref="AgentUpdater"/>.
///
/// Installed versions and paths are tracked locally (not in the manifest) since this process
/// is the only thing that knows what's actually on disk on this machine.
/// </summary>
public class UpdateCheckService : BackgroundService
{
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceUpdater _serviceUpdater;
    private readonly AgentUpdater _agentUpdater;

    public UpdateCheckService(
        ILogger<UpdateCheckService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ServiceUpdater serviceUpdater,
        AgentUpdater agentUpdater)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _serviceUpdater = serviceUpdater;
        _agentUpdater = agentUpdater;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkInterval = TimeSpan.FromMinutes(_configuration.GetValue("CheckIntervalMinutes", 60));
        var serverBaseUrl = _configuration["ServerBaseUrl"] ?? "https://localhost:44323";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndApplyUpdatesAsync(serverBaseUrl, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update check failed; will retry on the next interval.");
            }

            await Task.Delay(checkInterval, stoppingToken);
        }
    }

    private async Task CheckAndApplyUpdatesAsync(string serverBaseUrl, CancellationToken stoppingToken)
    {
        var client = _httpClientFactory.CreateClient();
        var manifest = await client.GetFromJsonAsync<UpdateManifestDto>($"{serverBaseUrl}/api/updates/latest", stoppingToken);
        if (manifest is null)
        {
            _logger.LogWarning("Update manifest request returned no content.");
            return;
        }

        await TryApplyComponentUpdateAsync(
            componentName: "Service",
            update: manifest.Service,
            installedVersionPath: _configuration["Service:InstalledVersionFile"],
            installDirectory: _configuration["Service:InstallDirectory"],
            apply: async downloadedZipPath =>
            {
                var installDirectory = _configuration["Service:InstallDirectory"];
                var executablePath = _configuration["Service:ExecutablePath"];
                if (string.IsNullOrEmpty(installDirectory) || string.IsNullOrEmpty(executablePath))
                {
                    _logger.LogWarning("Service:InstallDirectory/Service:ExecutablePath are not configured; skipping Service update/install.");
                    return false;
                }

                return await Task.Run(() => _serviceUpdater.ApplyUpdate(downloadedZipPath, installDirectory, executablePath));
            },
            stoppingToken);

        await TryApplyComponentUpdateAsync(
            componentName: "Agent",
            update: manifest.Agent,
            installedVersionPath: _configuration["Agent:InstalledVersionFile"],
            installDirectory: _configuration["Agent:InstallDirectory"],
            apply: async downloadedZipPath =>
            {
                var installDirectory = _configuration["Agent:InstallDirectory"];
                var executablePath = _configuration["Agent:ExecutablePath"];
                if (string.IsNullOrEmpty(installDirectory) || string.IsNullOrEmpty(executablePath))
                {
                    _logger.LogWarning("Agent:InstallDirectory/Agent:ExecutablePath are not configured; skipping Agent update.");
                    return false;
                }

                return await Task.Run(() => _agentUpdater.ApplyUpdate(downloadedZipPath, installDirectory, executablePath));
            },
            stoppingToken);
    }

    private async Task TryApplyComponentUpdateAsync(
        string componentName,
        ComponentUpdateInfo update,
        string? installedVersionPath,
        string? installDirectory,
        Func<string, Task<bool>> apply,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return;
        }

        if (!System.Version.TryParse(update.Version, out var latestVersion))
        {
            _logger.LogWarning("{Component} manifest version {Version} is not a valid version string.", componentName, update.Version);
            return;
        }

        var installedVersion = ReadInstalledVersion(installedVersionPath);
        if (installedVersion is not null && installedVersion >= latestVersion)
        {
            return;
        }

        _logger.LogInformation("Newer {Component} version available: {Installed} -> {Latest}.", componentName, installedVersion, latestVersion);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"screenbux-{componentName.ToLowerInvariant()}-{Guid.NewGuid():N}.zip");
        try
        {
            var client = _httpClientFactory.CreateClient();
            await using (var responseStream = await client.GetStreamAsync(update.DownloadUrl, stoppingToken))
            await using (var fileStream = File.Create(tempZipPath))
            {
                await responseStream.CopyToAsync(fileStream, stoppingToken);
            }

            var applied = await apply(tempZipPath);
            if (applied)
            {
                WriteInstalledVersion(installedVersionPath, latestVersion);
                _logger.LogInformation("{Component} updated to version {Version}.", componentName, latestVersion);
            }
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
    }

    private System.Version? ReadInstalledVersion(string? installedVersionPath)
    {
        if (string.IsNullOrEmpty(installedVersionPath) || !File.Exists(installedVersionPath))
        {
            return null;
        }

        return System.Version.TryParse(File.ReadAllText(installedVersionPath).Trim(), out var version) ? version : null;
    }

    private void WriteInstalledVersion(string? installedVersionPath, System.Version version)
    {
        if (string.IsNullOrEmpty(installedVersionPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(installedVersionPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(installedVersionPath, version.ToString());
    }
}
