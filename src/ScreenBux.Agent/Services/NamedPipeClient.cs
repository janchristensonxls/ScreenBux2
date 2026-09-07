using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Client for communicating with the Windows Service via Named Pipes
/// </summary>
public class NamedPipeClient
{
    private const string PipeName = "ScreenBuxServicePipe";
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sends a message to the service and waits for a response
    /// </summary>
    public async Task<T?> SendMessageAsync<T>(object message) where T : class
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync((int)_connectionTimeout.TotalMilliseconds);

            // Switch to message mode so IsMessageComplete works correctly.
            // The server runs in PipeTransmissionMode.Message; the client must opt-in after connecting.
            if (OperatingSystem.IsWindows())
            {
                pipeClient.ReadMode = PipeTransmissionMode.Message;
            }

            // Serialize and send message
            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);
            await pipeClient.FlushAsync();

            // Read response
            var buffer = new byte[4096];
            var responseBuilder = new StringBuilder();
            int bytesRead;

            do
            {
                bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length);
                responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            } while (!pipeClient.IsMessageComplete);

            var responseJson = responseBuilder.ToString();
            return JsonSerializer.Deserialize<T>(responseJson);
        }
        catch (TimeoutException)
        {
            // Service not running or not responding
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NamedPipeClient] Error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends a message to the service and waits for a response whose concrete type is
    /// determined by the response's own "MessageType" discriminator (e.g. a ProcessReport can
    /// come back as either a plain CommandResponse or a CloseProcessCommand fallback).
    /// Deserializing straight to <c>object</c> doesn't work with System.Text.Json - it just
    /// yields a boxed JsonElement, not the polymorphic type - so the caller must route by
    /// MessageType itself.
    /// </summary>
    public async Task<ScreenBux.Shared.Contracts.INamedPipeMessage?> SendMessageForPolymorphicResponseAsync(object message)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync((int)_connectionTimeout.TotalMilliseconds);

            if (OperatingSystem.IsWindows())
            {
                pipeClient.ReadMode = PipeTransmissionMode.Message;
            }

            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);
            await pipeClient.FlushAsync();

            var buffer = new byte[4096];
            var responseBuilder = new StringBuilder();
            int bytesRead;

            do
            {
                bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length);
                responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            } while (!pipeClient.IsMessageComplete);

            var responseJson = responseBuilder.ToString();
            if (string.IsNullOrEmpty(responseJson))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var messageType = doc.RootElement.GetProperty("MessageType").GetString();

            return messageType switch
            {
                "CloseProcess" => JsonSerializer.Deserialize<CloseProcessCommand>(responseJson),
                "Response" => JsonSerializer.Deserialize<CommandResponse>(responseJson),
                _ => null
            };
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NamedPipeClient] Error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if the service is available
    /// </summary>
    public async Task<bool> IsServiceAvailableAsync()
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(1000);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
