using ScreenBux.Service.Services;

namespace ScreenBux.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly PolicyService _policyService;

    public Worker(ILogger<Worker> logger, PolicyService policyService)
    {
        _logger = logger;
        _policyService = policyService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScreenBux Service Worker started at: {time}", DateTimeOffset.Now);

        // The NamedPipeServerService handles the main communication
        // This worker can be used for periodic tasks like policy reloading
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            
            // Periodically reload policy configuration
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Worker heartbeat at: {time}", DateTimeOffset.Now);
            }
        }

        _logger.LogInformation("ScreenBux Service Worker stopping at: {time}", DateTimeOffset.Now);
    }
}
