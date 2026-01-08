namespace ScreenBux.Shared.Contracts;

/// <summary>
/// Base interface for messages sent through named pipes
/// </summary>
public interface INamedPipeMessage
{
    string MessageType { get; }
    DateTime Timestamp { get; }
}
