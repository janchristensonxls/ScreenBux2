using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ScreenBux.Shared.Messages;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Named Pipe server for communication with the Windows Agent
/// </summary>
public class NamedPipeServerService : BackgroundService
{
    private readonly ILogger<NamedPipeServerService> _logger;
    private readonly PolicyService _policyService;
    private readonly GrantService _grantService;
    private readonly UsageTrackingService _usageTracking;
    private readonly ProcessKillerService _processKiller;
    private readonly DevicePolicySyncService _devicePolicySync;
    private readonly PowerActionService _powerAction;
    private readonly PolicySyncService _policySync;
    private readonly PendingWindowListRequestCoordinator _windowListCoordinator;
    private readonly PendingScreenCaptureRequestCoordinator _screenCaptureCoordinator;
    private const string PipeName = "ScreenBuxServicePipe";

    public NamedPipeServerService(
        ILogger<NamedPipeServerService> logger,
        PolicyService policyService,
        GrantService grantService,
        UsageTrackingService usageTracking,
        ProcessKillerService processKiller,
        DevicePolicySyncService devicePolicySync,
        PowerActionService powerAction,
        PolicySyncService policySync,
        PendingWindowListRequestCoordinator windowListCoordinator,
        PendingScreenCaptureRequestCoordinator screenCaptureCoordinator)
    {
        _logger = logger;
        _policyService = policyService;
        _grantService = grantService;
        _usageTracking = usageTracking;
        _processKiller = processKiller;
        _devicePolicySync = devicePolicySync;
        _powerAction = powerAction;
        _policySync = policySync;
        _windowListCoordinator = windowListCoordinator;
        _screenCaptureCoordinator = screenCaptureCoordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named Pipe Server started on pipe: {PipeName}", PipeName);

        // Load policy at startup
        await _policyService.LoadPolicyAsync();
        await _grantService.LoadGrantAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte;
                if (OperatingSystem.IsWindows())
                {
                    transmissionMode = PipeTransmissionMode.Message;
                }

                // The Service normally runs elevated (LocalSystem/Administrator via the SCM),
                // while the Agent runs as an ordinary logged-in user. NamedPipeServerStream's
                // default ACL only grants access to the creating account (and admins), so
                // without an explicit, more permissive PipeSecurity here the Agent's
                // ConnectAsync would be denied at the OS level - surfacing to the Agent only as
                // a silent timeout/failure, never as a clear "access denied" message anywhere.
                // Grant read/write to Authenticated Users so a normal user session can connect.
                NamedPipeServerStream pipeServer;
                if (OperatingSystem.IsWindows())
                {
                    var pipeSecurity = new PipeSecurity();

                    // Grant read/write to Authenticated Users so a normal user session (the
                    // Agent) can connect, without needing to be an administrator.
                    var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        authenticatedUsers,
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

                    // A custom PipeSecurity REPLACES the default ACL entirely rather than
                    // extending it, so the Service's own account (LocalSystem when running as
                    // a Windows Service, or the interactive admin account when run via `dotnet
                    // run`) must be explicitly re-granted full control here - otherwise pipe
                    // creation itself fails with UnauthorizedAccessException, since the
                    // creating process no longer has rights to its own pipe.
                    var currentOwner = WindowsIdentity.GetCurrent().User;
                    if (currentOwner is not null)
                    {
                        pipeSecurity.AddAccessRule(new PipeAccessRule(
                            currentOwner,
                            PipeAccessRights.FullControl,
                            AccessControlType.Allow));
                    }

                    var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        localSystem,
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        administrators,
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    pipeServer = NamedPipeServerStreamAcl.Create(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        transmissionMode,
                        PipeOptions.Asynchronous,
                        inBufferSize: 0,
                        outBufferSize: 0,
                        pipeSecurity: pipeSecurity);
                }
                else
                {
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        transmissionMode,
                        PipeOptions.Asynchronous);
                }

                try
                {
                    _logger.LogDebug("Waiting for client connection...");
                    await pipeServer.WaitForConnectionAsync(stoppingToken);
                    _logger.LogDebug("Client connected to named pipe");
                }
                catch
                {
                    // Connection wait failed or was cancelled (e.g. shutdown);
                    // dispose the orphaned pipe and let the outer handler react.
                    await pipeServer.DisposeAsync();
                    throw;
                }

                // Ownership of the pipe is transferred to the handler, which disposes it
                // when the client disconnects. Do NOT wrap the pipe in a 'using' here:
                // the loop would dispose it while the background handler is still reading.
                _ = Task.Run(() => HandleClientAsync(pipeServer, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Named Pipe Server stopping...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Named Pipe Server");
                await Task.Delay(1000, stoppingToken); // Brief delay before retry
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        await using (pipeServer)
        {
            try
            {
                while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var buffer = new byte[4096];
                    var messageBuilder = new StringBuilder();
                    int bytesRead;

                    do
                    {
                        bytesRead = await pipeServer.ReadAsync(buffer, cancellationToken);

                        // A zero-byte read means the client closed the pipe. Stop here
                        // instead of touching IsMessageComplete on a broken/closed pipe.
                        if (bytesRead == 0)
                        {
                            return;
                        }

                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    } while (!pipeServer.IsMessageComplete);

                    var messageJson = messageBuilder.ToString();
                    if (string.IsNullOrEmpty(messageJson))
                        continue;

                    _logger.LogDebug("Received message: {Message}", messageJson);

                    // Process the message and get response
                    var response = await ProcessMessageAsync(messageJson);

                    // Send response back
                    var responseJson = JsonSerializer.Serialize(response);
                    var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                    await pipeServer.WriteAsync(responseBytes, cancellationToken);
                    await pipeServer.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Service is shutting down - expected, nothing to do.
            }
            catch (IOException ex)
            {
                // Broken pipe / client disconnected mid-message - expected during normal operation.
                _logger.LogDebug(ex, "Named pipe connection closed by client");
            }
            catch (ObjectDisposedException)
            {
                // Pipe disposed during shutdown - expected, nothing to do.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error handling client connection");
            }
        }
    }

    private async Task<object> ProcessMessageAsync(string messageJson)
    {
        try
        {
            // Parse the message type
            using var doc = JsonDocument.Parse(messageJson);
            var messageType = doc.RootElement.GetProperty("MessageType").GetString();

            switch (messageType)
            {
                case "ProcessReport":
                    var reportMessage = JsonSerializer.Deserialize<ProcessReportMessage>(messageJson);
                    var reportResponse = await HandleProcessReportAsync(reportMessage);
                    return StampPendingWindowListRequest(reportResponse);

                case "GetPolicy":
                    return HandleGetPolicyRequest();

                case "LinkDevice":
                    var linkRequest = JsonSerializer.Deserialize<LinkDeviceRequest>(messageJson);
                    return await HandleLinkDeviceAsync(linkRequest);

                case "GrantStatusRequest":
                    return new GrantStatusResponse { ExpiresAtUtc = _grantService.ExpiresAtUtc };

                case "WindowListReport":
                    var windowListReport = JsonSerializer.Deserialize<WindowListReportMessage>(messageJson);
                    return HandleWindowListReport(windowListReport);

                case "ScreenCaptureReport":
                    var screenCaptureReport = JsonSerializer.Deserialize<ScreenCaptureReportMessage>(messageJson);
                    return HandleScreenCaptureReport(screenCaptureReport);

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", messageType);
                    return new CommandResponse
                    {
                        Success = false,
                        Message = $"Unknown message type: {messageType}"
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return new CommandResponse
            {
                Success = false,
                Message = $"Error processing message: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handles a foreground process/window report from the Agent (the only reliable source
    /// of foreground-window/title information, since this Service - when running as a real
    /// Windows Service - runs in Session 0 and cannot see the interactive user's desktop).
    /// This is the sole place where WindowTitleRegex rules are meaningfully evaluated.
    ///
    /// Enforcement itself is performed here, by the Service, via <see cref="ProcessKillerService"/> -
    /// not by asking the Agent to close the process. Windows processes are machine-global,
    /// so the Service can open a handle to the reported PID directly; doing the kill here lets
    /// enforcement benefit from whatever elevated rights the Service runs with (e.g. LocalSystem
    /// when installed as a real Windows Service), which the Agent's normal user-session token may
    /// lack for admin-launched or otherwise protected processes. The Agent is only asked to act
    /// as a fallback for a graceful, UI-level close (WM_CLOSE via CloseMainWindow) if the
    /// Service's own attempt fails - that's a legitimate user-session action that doesn't need
    /// elevation, unlike forceful termination.
    /// </summary>
    private async Task<object> HandleProcessReportAsync(ProcessReportMessage? message)
    {
        if (message?.Process == null)
        {
            return new CommandResponse
            {
                Success = false,
                Message = "Invalid process report"
            };
        }

        _logger.LogInformation("Process reported: {ProcessName} (PID: {ProcessId}, Title: {WindowTitle})",
            message.Process.ProcessName, message.Process.ProcessId, message.Process.WindowTitle);

        if (_grantService.IsGrantActive)
        {
            return new CommandResponse
            {
                Success = true,
                Message = "Time grant active; enforcement paused"
            };
        }

        var category = _policyService.ClassifyProcess(message.Process, isForegroundWindow: true);
        _usageTracking.ReportForegroundCategory(category?.Name);

        var categoryPolicy = _policyService.GetCategoryPolicy(category?.Name);
        var shouldBlock = PolicyService.IsBlockedByPolicy(categoryPolicy, _usageTracking.GetTotalSecondsTodayForCategories(categoryPolicy.CategoryNames));
        var dbg = !string.IsNullOrEmpty(message.Process.WindowTitle) && message.Process.WindowTitle.Contains("tube");
        if (dbg)
        {
            var stp = 56;
        }
        if (shouldBlock)
        {
            var reason = category != null
                ? $"Category '{category.Name}' blocked by parental control policy"
                : "Application blocked by parental control policy";
            var action = categoryPolicy.Action;

            _logger.LogWarning("Process {ProcessName} (PID: {ProcessId}) violates policy ({Reason}), enforcing {Action}",
                message.Process.ProcessName, message.Process.ProcessId, reason, action);

            if (action == PolicyRuleAction.KillProcessTree)
            {
                var killed = await _processKiller.KillProcessTreeAsync(message.Process.ProcessId, reason);
                if (killed && !_processKiller.IsDryRun)
                {
                    await ReportDetectionAsync(message.Process);
                }

                return new CommandResponse
                {
                    Success = killed,
                    Message = _processKiller.IsDryRun
                        ? $"[DRY-RUN] Would kill process tree, reason: {reason}"
                        : killed ? $"Process tree killed by Service, reason: {reason}" : "Failed to kill process tree"
                };
            }

            var closed = await _processKiller.TryCloseProcessAsync(message.Process.ProcessId, reason);

            if (closed)
            {
                if (!_processKiller.IsDryRun)
                {
                    await ReportDetectionAsync(message.Process);
                }

                return new CommandResponse
                {
                    Success = true,
                    Message = _processKiller.IsDryRun
                        ? $"[DRY-RUN] Would close process, reason: {reason}"
                        : $"Process closed by Service, reason: {reason}"
                };
            }

            // The Service couldn't terminate it directly (e.g. access denied / protected
            // process). Fall back to asking the Agent to try a graceful, in-session close -
            // it can't forcefully kill a protected process either, but CloseMainWindow() is
            // a normal UI action that doesn't require elevation and may still succeed.
            _logger.LogWarning(
                "Service-side enforcement failed for process {ProcessName} (PID: {ProcessId}); falling back to Agent-side graceful close",
                message.Process.ProcessName, message.Process.ProcessId);

            return new CloseProcessCommand
            {
                ProcessId = message.Process.ProcessId,
                Reason = reason
            };
        }

        return new CommandResponse
        {
            Success = true,
            Message = "Process allowed"
        };
    }

    /// <summary>
    /// Relays a foreground process closure - reported by the Agent and enforced here by the
    /// Service - to the monitoring hub, mirroring what <see cref="ProcessMonitoringService"/>
    /// does for its own background-enumeration kills. Without this, closures triggered via the
    /// Named Pipe (foreground/window-title matches) never show up in the WebClient monitoring view.
    /// </summary>
    private async Task ReportDetectionAsync(ProcessInfo processInfo)
    {
        processInfo.DetectedAt = DateTime.UtcNow;
        await _policySync.SendProcessDetectionAsync(processInfo);
    }

    private object HandleGetPolicyRequest()
    {
        return new PolicyResponse
        {
            Configuration = _policyService.GetConfiguration()
        };
    }

    /// <summary>
    /// Piggybacks any currently-pending on-demand "get window list" request onto the given
    /// response, if it's a plain <see cref="CommandResponse"/> (i.e. not a <see cref="CloseProcessCommand"/>
    /// fallback, which the Agent handles specially and doesn't check for this flag). Avoids
    /// needing a persistent duplex pipe connection - the Agent already polls every couple of
    /// seconds via <see cref="ProcessReportMessage"/>, so this rides along on that.
    /// </summary>
    private object StampPendingWindowListRequest(object response)
    {
        if (response is CommandResponse commandResponse)
        {
            commandResponse.PendingWindowListRequestId = _windowListCoordinator.TryGetNextPendingRequestId();
            commandResponse.PendingScreenCaptureRequestId = _screenCaptureCoordinator.TryGetNextPendingRequestId();
        }

        return response;
    }

    private object HandleWindowListReport(WindowListReportMessage? message)
    {
        if (message is null)
        {
            return new CommandResponse { Success = false, Message = "Invalid window list report" };
        }

        _windowListCoordinator.Complete(message.RequestId, message.Windows);
        return new CommandResponse { Success = true };
    }

    private object HandleScreenCaptureReport(ScreenCaptureReportMessage? message)
    {
        if (message is null)
        {
            return new CommandResponse { Success = false, Message = "Invalid screen capture report" };
        }

        _screenCaptureCoordinator.Complete(message.RequestId, message.Success ? message.Images : null);
        return new CommandResponse { Success = true };
    }

    private async Task<LinkDeviceResponse> HandleLinkDeviceAsync(LinkDeviceRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.LinkCode))
        {
            return new LinkDeviceResponse { Success = false, Message = "Link code is required." };
        }

        _logger.LogInformation("Link-device request received for code {Code}.", request.LinkCode);
        var (success, message, deviceId) = await _devicePolicySync.RedeemCodeAsync(request.LinkCode);

        return new LinkDeviceResponse
        {
            Success = success,
            Message = message,
            DeviceId = deviceId
        };
    }
}
