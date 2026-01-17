namespace ScreenBux.WebServer.Contracts;

public record RegisterRequest(string Email, string Password, string HouseholdName);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string UserId, string Email);
