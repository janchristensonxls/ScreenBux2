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
        catch (Exception)
        {
            // Connection error
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
