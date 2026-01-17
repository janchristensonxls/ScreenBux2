namespace ScreenBux.WebServer.Contracts;

public record CreatePairingCodeRequest(Guid HouseholdId);

public record PairDeviceRequest(
    string PairingCode,
    Guid DeviceId,
    string DeviceSecret,
    string DeviceName,
    string? MachineName,
    string? OsVersion,
    string? AgentVersion,
    string? ServiceVersion);

public record DeviceAuthRequest(
    Guid DeviceId,
    string ClientType,
    DateTime Timestamp,
    string Nonce,
    string Signature);

public record DeviceAuthResponse(string Token, DateTime ExpiresAt);

public record DeviceSummary(
    Guid DeviceId,
    string DisplayName,
    string? MachineName,
    string? OsVersion,
    string? AgentVersion,
    string? ServiceVersion,
    bool OnlineAgent,
    bool OnlineService,
    DateTime? LastSeenAt);
