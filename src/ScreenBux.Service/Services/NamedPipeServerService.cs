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
    private const string PipeName = "ScreenBuxServicePipe";

    public NamedPipeServerService(
        ILogger<NamedPipeServerService> logger,
        PolicyService policyService,
        ProcessKillerService processKiller)
    {
        _logger = logger;
        _policyService = policyService;
        _processKiller = processKiller;
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

                await using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode,
                    PipeOptions.Asynchronous);

                _logger.LogDebug("Waiting for client connection...");
                await pipeServer.WaitForConnectionAsync(stoppingToken);
                _logger.LogInformation("Client connected to named pipe");

                // Handle the connection in a separate task
                _ = Task.Run(async () => await HandleClientAsync(pipeServer, stoppingToken), stoppingToken);
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
        try
        {
            while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var buffer = new byte[4096];
                var messageBuilder = new StringBuilder();
                int bytesRead;

                do
                {
                    bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
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
                await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                await pipeServer.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client connection");
        }
        finally
        {
            if (pipeServer.IsConnected)
            {
                pipeServer.Disconnect();
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

        _logger.LogInformation("Process reported: {ProcessName} (PID: {ProcessId})",
            message.Process.ProcessName, message.Process.ProcessId);

        // Check if process should be blocked
        if (_policyService.ShouldBlockProcess(message.Process))
        {
            _logger.LogWarning("Process {ProcessName} (PID: {ProcessId}) violates policy, requesting closure",
                message.Process.ProcessName, message.Process.ProcessId);

            // Send close command back
            return new CloseProcessCommand
            {
                ProcessId = message.Process.ProcessId,
                Reason = "Application blocked by parental control policy"
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
}
