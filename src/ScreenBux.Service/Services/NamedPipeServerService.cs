using System.IO.Pipes;
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
    private readonly ProcessKillerService _processKiller;
    private readonly DevicePolicySyncService _devicePolicySync;
    private const string PipeName = "ScreenBuxServicePipe";

    public NamedPipeServerService(
        ILogger<NamedPipeServerService> logger,
        PolicyService policyService,
        ProcessKillerService processKiller,
        DevicePolicySyncService devicePolicySync)
    {
        _logger = logger;
        _policyService = policyService;
        _processKiller = processKiller;
        _devicePolicySync = devicePolicySync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Named Pipe Server started on pipe: {PipeName}", PipeName);

        // Load policy at startup
        await _policyService.LoadPolicyAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte;
                if (OperatingSystem.IsWindows())
                {
                    transmissionMode = PipeTransmissionMode.Message;
                }

                var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode,
                    PipeOptions.Asynchronous);

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
                    return await HandleProcessReportAsync(reportMessage);

                case "GetPolicy":
                    return HandleGetPolicyRequest();

                case "LinkDevice":
                    var linkRequest = JsonSerializer.Deserialize<LinkDeviceRequest>(messageJson);
                    return await HandleLinkDeviceAsync(linkRequest);

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

        var rule = _policyService.GetMatchingRule(message.Process, isForegroundWindow: true);
        var shouldBlock = rule != null || _policyService.ShouldBlockProcess(message.Process, isForegroundWindow: true);

        if (shouldBlock)
        {
            var reason = rule?.Name ?? "Application blocked by parental control policy";

            _logger.LogWarning("Process {ProcessName} (PID: {ProcessId}) violates policy ({Reason}), requesting closure",
                message.Process.ProcessName, message.Process.ProcessId, reason);

            _processKiller.NotifyRemoteEnforcementAttempt(message.Process.ProcessId, message.Process.ProcessName, reason);

            if (_processKiller.IsDryRun)
            {
                return new CommandResponse
                {
                    Success = true,
                    Message = $"[DRY-RUN] Would close process, reason: {reason}"
                };
            }

            // Send close command back - the Agent performs the actual close, since it runs
            // in the interactive session and can access the window.
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

    private object HandleGetPolicyRequest()
    {
        return new PolicyResponse
        {
            Configuration = _policyService.GetConfiguration()
        };
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
