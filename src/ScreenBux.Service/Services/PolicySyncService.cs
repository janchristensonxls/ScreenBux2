using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Maintains the SignalR connection to the web server: receives policy updates
/// and relays process detections to the monitoring UI.
/// </summary>
public class PolicySyncService : BackgroundService
{
    private readonly ILogger<PolicySyncService> _logger;
    private readonly IConfiguration _configuration;
    private readonly PolicyService _policyService;
    private readonly GrantService _grantService;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly IServiceProvider _serviceProvider;
    private readonly PendingWindowListRequestCoordinator _windowListCoordinator;
    private readonly PendingScreenCaptureRequestCoordinator _screenCaptureCoordinator;
    private readonly PendingVersionInfoRequestCoordinator _versionInfoCoordinator;
    private readonly UsageActivityLogWriter _activityLog;
    private readonly IHttpClientFactory _httpClientFactory;
    private HubConnection? _hubConnection;

    /// <summary>Widest date span honored per usage-logs request, to bound how many day-files a single request reads.</summary>
    private const int MaxUsageLogsRequestDays = 31;

    public PolicySyncService(
        ILogger<PolicySyncService> logger,
        IConfiguration configuration,
        PolicyService policyService,
        GrantService grantService,
        DeviceIdentityService deviceIdentity,
        IServiceProvider serviceProvider,
        PendingWindowListRequestCoordinator windowListCoordinator,
        PendingScreenCaptureRequestCoordinator screenCaptureCoordinator,
        PendingVersionInfoRequestCoordinator versionInfoCoordinator,
        UsageActivityLogWriter activityLog,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _policyService = policyService;
        _grantService = grantService;
        _deviceIdentity = deviceIdentity;
        _serviceProvider = serviceProvider;
        _windowListCoordinator = windowListCoordinator;
        _screenCaptureCoordinator = screenCaptureCoordinator;
        _versionInfoCoordinator = versionInfoCoordinator;
        _activityLog = activityLog;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _configuration["MonitoringHubUrl"] ?? "https://localhost:44323/monitoringHub";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () =>
                {
                    var state = _deviceIdentity.GetOrCreate();
                    return Task.FromResult(state.DeviceToken);
                };
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<PolicyConfiguration>("PolicyUpdated", async policy =>
        {
            try
            {
                _logger.LogInformation("Policy update received from SignalR");
                await _policyService.UpdatePolicyAsync(policy);
                _policyService.MarkSyncedSinceStartup();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply policy update received from SignalR");
            }
        });

        _hubConnection.On<GrantDto>("GrantUpdated", async grant =>
        {
            try
            {
                _logger.LogInformation("Grant update received from SignalR; expires at {ExpiresAtUtc}", grant.ExpiresAtUtc);
                await _grantService.UpdateGrantAsync(grant.ExpiresAtUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply grant update received from SignalR");
            }
        });

        _hubConnection.On<Guid>("ProcessListRequested", async requestId =>
        {
            _logger.LogInformation("Process list requested via SignalR (request {RequestId}).", requestId);
            await HandleProcessListRequestedAsync(requestId);
        });

        _hubConnection.On<Guid>("ScreenCaptureRequested", async requestId =>
        {
            _logger.LogInformation("Screen capture requested via SignalR (request {RequestId}).", requestId);
            await HandleScreenCaptureRequestedAsync(requestId);
        });

        _hubConnection.On<Guid, DateOnly, DateOnly>("UsageLogsRequested", async (requestId, startDate, endDate) =>
        {
            _logger.LogInformation("Usage logs requested via SignalR (request {RequestId}, {StartDate} to {EndDate}).", requestId, startDate, endDate);
            await HandleUsageLogsRequestedAsync(requestId, startDate, endDate);
        });

        _hubConnection.On<Guid>("VersionInfoRequested", async requestId =>
        {
            _logger.LogInformation("Version info requested via SignalR (request {RequestId}).", requestId);
            await HandleVersionInfoRequestedAsync(requestId);
        });

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "SignalR connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _logger.LogInformation("SignalR reconnected");
            return Task.CompletedTask;
        };

        _hubConnection.Closed += async error =>
        {
            _logger.LogWarning(error, "SignalR connection closed");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until this device is linked and has a token before connecting.
            var state = _deviceIdentity.GetOrCreate();
            if (string.IsNullOrEmpty(state.DeviceToken))
            {
                _logger.LogDebug("Device not yet linked; waiting before connecting to SignalR hub.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                await _hubConnection.StartAsync(stoppingToken);
                _logger.LogInformation("SignalR connected to {HubUrl}", hubUrl);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to SignalR, retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // ignore
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync(cancellationToken);
            await _hubConnection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Relays a process detection to the web server hub so the monitoring UI updates live.
    /// No-op when the hub connection is not established.
    /// </summary>
    public async Task SendProcessDetectionAsync(ProcessInfo processInfo)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            await connection.InvokeAsync("BroadcastProcessDetection", processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send process detection to hub");
        }
    }

    /// <summary>
    /// Handles an on-demand "get process list" request pushed from the web server's hub.
    /// Resolves <see cref="ProcessMonitoringService"/> lazily via <see cref="_serviceProvider"/>
    /// rather than a constructor dependency, since ProcessMonitoringService itself depends on
    /// this class (to report detections) and a direct two-way constructor dependency would be
    /// a circular reference.
    ///
    /// The base list from <see cref="ProcessMonitoringService.GetCurrentProcesses"/> is already
    /// restricted to the interactive session, but the Service (running in Session 0) has no way
    /// to know which of those processes actually have a visible window - only the Agent, which
    /// runs inside that session, can see that. So a window-list request is registered with
    /// <see cref="_windowListCoordinator"/> and piggybacked onto the Agent's next poll response
    /// (see <see cref="NamedPipeServerService"/>); the result is used both to attach real window
    /// titles and to filter the final list down to windowed, user-facing processes only.
    /// </summary>
    private async Task HandleProcessListRequestedAsync(Guid requestId)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        var deviceId = _deviceIdentity.GetOrCreate().DeviceId;
        var result = new ProcessListResult
        {
            RequestId = requestId,
            DeviceId = deviceId
        };

        try
        {
            var processMonitoring = _serviceProvider.GetRequiredService<ProcessMonitoringService>();

            // Register the pending request BEFORE doing any other work, so it's visible to the
            // Agent's very next poll tick as early as possible - GetCurrentProcesses() below can
            // take a non-trivial amount of time (resolving MainModule.FileName for every process
            // in the session), and registering after it would risk missing a tick and needlessly
            // falling back to the unfiltered list.
            _windowListCoordinator.Register(requestId);

            var candidates = processMonitoring.GetCurrentProcesses();

            // Give the Agent up to a couple of poll ticks to pick up the pending request and
            // report back, rather than blocking indefinitely if it's not running/connected.
            var windows = await _windowListCoordinator.WaitAsync(
                requestId, TimeSpan.FromSeconds(8), CancellationToken.None);

            if (windows != null)
            {
                var titlesByProcessId = windows
                    .GroupBy(w => w.ProcessId)
                    .ToDictionary(g => g.Key, g => g.First().WindowTitle);

                foreach (var process in candidates)
                {
                    if (titlesByProcessId.TryGetValue(process.ProcessId, out var title))
                    {
                        process.WindowTitle = title;
                    }
                }

                // Only the Agent can tell us which processes have a visible window; without a
                // response, fall back to showing the (session-filtered) list unfiltered rather
                // than hiding everything.
                result.Processes.AddRange(candidates.Where(p => titlesByProcessId.ContainsKey(p.ProcessId)));
            }
            else
            {
                _logger.LogWarning(
                    "No window list response from Agent for request {RequestId}; returning unfiltered process list.",
                    requestId);
                result.Processes.AddRange(candidates);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate processes for on-demand process list request {RequestId}.", requestId);
            result.Success = false;
            result.ErrorMessage = "Failed to enumerate processes on this device.";
        }

        try
        {
            await connection.InvokeAsync("ReportProcessList", result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send process list result to hub for request {RequestId}.", requestId);
        }
    }

    /// <summary>
    /// Handles an on-demand "get version" request pushed from the web server's hub. Reports
    /// both the Service's auto-updater-installed version and its currently running assembly
    /// version (they can legitimately differ - see <see cref="GetInstalledServiceVersion"/>),
    /// plus the same pair for the Agent, obtained by round-tripping a request through the
    /// named pipe: registered with <see cref="_versionInfoCoordinator"/> and piggybacked onto
    /// the Agent's next poll response (see <see cref="NamedPipeServerService"/>), mirroring
    /// <see cref="HandleProcessListRequestedAsync"/>.
    /// </summary>
    private async Task HandleVersionInfoRequestedAsync(Guid requestId)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        var deviceId = _deviceIdentity.GetOrCreate().DeviceId;
        var result = new VersionInfoResult
        {
            RequestId = requestId,
            DeviceId = deviceId,
            ServiceInstalledVersion = GetInstalledServiceVersion(),
            ServiceRunningVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        };

        try
        {
            // Register before doing any other work, so the Agent's next poll tick has the
            // maximum chance of picking up this request before the wait below times out.
            _versionInfoCoordinator.Register(requestId);

            var agentReport = await _versionInfoCoordinator.WaitAsync(
                requestId, TimeSpan.FromSeconds(8), CancellationToken.None);

            if (agentReport is { Success: true })
            {
                result.AgentInstalledVersion = agentReport.AgentInstalledVersion;
                result.AgentRunningVersion = agentReport.AgentRunningVersion;
            }
            else
            {
                _logger.LogWarning(
                    "No version info response from Agent for request {RequestId}.", requestId);
                result.ErrorMessage = "Agent did not respond with its version (it may not be running).";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather version info for on-demand request {RequestId}.", requestId);
            result.Success = false;
            result.ErrorMessage = "Failed to gather version info on this device.";
        }

        try
        {
            await connection.InvokeAsync("ReportVersionInfo", result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send version info result to hub for request {RequestId}.", requestId);
        }
    }

    /// <summary>
    /// Resolves the version the auto-updater (ScreenBux.Updater) actually considers "installed"
    /// on this machine - the marker file it writes on a successful update - which is reported
    /// alongside (not instead of) the running assembly version in <see cref="HandleVersionInfoRequestedAsync"/>,
    /// since the two can legitimately differ (an update pending a restart, a marker that's stale,
    /// or a dev "dotnet run" that's never touched by the Updater at all).
    /// </summary>
    private string? GetInstalledServiceVersion()
    {
        var path = _configuration["Service:InstalledVersionFile"] ?? Shared.Utilities.VersionFileStorage.GetServiceVersionFilePath();
        return Shared.Utilities.VersionFileStorage.TryReadVersion(path)?.ToString();
    }

    /// <summary>
    /// Handles an on-demand "capture all screens" request pushed from the web server's hub.
    /// Registers the request with <see cref="_screenCaptureCoordinator"/>, waits for the Agent
    /// to capture and report images back over the named pipe (see <see cref="NamedPipeServerService"/>),
    /// then POSTs the resulting images to the WebServer's REST endpoint (not over SignalR, to
    /// avoid pushing large binary payloads through the hub) and finally notifies the WebClient
    /// that the images are ready to download.
    /// </summary>
    private async Task HandleScreenCaptureRequestedAsync(Guid requestId)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        // Register before doing any other work, so the Agent's next poll tick has the maximum
        // chance of picking up this request before the wait below times out.
        _screenCaptureCoordinator.Register(requestId);

        var images = await _screenCaptureCoordinator.WaitAsync(
            requestId, TimeSpan.FromSeconds(15), CancellationToken.None);

        if (images is null)
        {
            _logger.LogWarning("No screen capture response from Agent for request {RequestId}.", requestId);
            return;
        }

        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            _logger.LogWarning("ServerBaseUrl not configured; cannot upload screen capture for request {RequestId}.", requestId);
            return;
        }

        var state = _deviceIdentity.GetOrCreate();
        if (!state.IsLinked)
        {
            _logger.LogWarning("Device not linked; cannot upload screen capture for request {RequestId}.", requestId);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", state.DeviceToken);

            var uploadRequest = new
            {
                DeviceId = state.DeviceId,
                Success = true,
                ErrorMessage = (string?)null,
                Images = images
            };

            using var response = await client.PostAsJsonAsync($"api/screencapture/{requestId}", uploadRequest);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to upload screen capture for request {RequestId} ({Status}).", requestId, (int)response.StatusCode);
                return;
            }

            await connection.InvokeAsync("ReportScreenCaptureReady", requestId, images.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload/report screen capture for request {RequestId}.", requestId);
        }
    }

    /// <summary>
    /// Handles an on-demand "fetch usage logs" request pushed from the web server's hub, for an
    /// inclusive local date range. Unlike <see cref="HandleScreenCaptureRequestedAsync"/>/
    /// <see cref="HandleProcessListRequestedAsync"/>, there's no Agent/named-pipe round trip
    /// here - the day-log files already live on this device (written by
    /// <see cref="_activityLog"/>), so the Service can read and upload them directly.
    /// </summary>
    private async Task HandleUsageLogsRequestedAsync(Guid requestId, DateOnly startDate, DateOnly endDate)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            _logger.LogWarning("ServerBaseUrl not configured; cannot upload usage logs for request {RequestId}.", requestId);
            return;
        }

        var state = _deviceIdentity.GetOrCreate();
        if (!state.IsLinked)
        {
            _logger.LogWarning("Device not linked; cannot upload usage logs for request {RequestId}.", requestId);
            return;
        }

        if (endDate < startDate)
        {
            (startDate, endDate) = (endDate, startDate);
        }

        // Clamp to a sane span (keeping the more recent end fixed) so a malformed or malicious
        // range can't force reading an unbounded number of day files.
        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxUsageLogsRequestDays)
        {
            startDate = endDate.AddDays(-(MaxUsageLogsRequestDays - 1));
        }

        var days = new List<UsageDayLog>();
        try
        {
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var entries = _activityLog.ReadDay(date);
                if (entries.Count > 0)
                {
                    days.Add(new UsageDayLog { EffectiveDate = date, Entries = entries.ToList() });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read local usage logs for request {RequestId}.", requestId);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", state.DeviceToken);

            var uploadRequest = new
            {
                DeviceId = state.DeviceId,
                Success = true,
                ErrorMessage = (string?)null,
                Days = days
            };

            using var response = await client.PostAsJsonAsync($"api/usagelogs/{requestId}", uploadRequest);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to upload usage logs for request {RequestId} ({Status}).", requestId, (int)response.StatusCode);
                return;
            }

            await connection.InvokeAsync("ReportUsageLogsReady", requestId, days.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload/report usage logs for request {RequestId}.", requestId);
        }
    }
}
