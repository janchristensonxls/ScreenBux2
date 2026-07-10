using System.Text.Json;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Persisted device identity/state stored next to the local policy cache.
/// </summary>
public class DeviceState
{
    public Guid DeviceId { get; set; }
    public string MachineKey { get; set; } = string.Empty;
    public string? DeviceToken { get; set; }
    public bool IsLinked => !string.IsNullOrEmpty(DeviceToken);
}

/// <summary>
/// Generates and persists a stable device identity (DeviceId + MachineKey) on first run,
/// and stores the device token obtained when linked to an account.
/// </summary>
public class DeviceIdentityService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<DeviceIdentityService> _logger;
    private readonly string _stateFilePath;
    private DeviceState? _state;

    public DeviceIdentityService(ILogger<DeviceIdentityService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var policyPath = configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
        var directory = Path.GetDirectoryName(policyPath) ?? AppContext.BaseDirectory;
        _stateFilePath = Path.Combine(directory, "device.json");
    }

    public DeviceState GetOrCreate()
    {
        if (_state is not null)
        {
            return _state;
        }

        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                _state = JsonSerializer.Deserialize<DeviceState>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read device state; regenerating.");
            }
        }

        if (_state is null || _state.DeviceId == Guid.Empty || string.IsNullOrEmpty(_state.MachineKey))
        {
            _state = new DeviceState
            {
                DeviceId = Guid.NewGuid(),
                MachineKey = Guid.NewGuid().ToString("N")
            };
            Save(_state);
            _logger.LogInformation("Generated new device identity {DeviceId}", _state.DeviceId);
        }

        return _state;
    }

    public void Save(DeviceState state)
    {
        _state = state;
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, SerializerOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist device state.");
        }
    }
}
