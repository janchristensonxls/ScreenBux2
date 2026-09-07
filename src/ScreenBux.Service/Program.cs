using ScreenBux.Service;
using ScreenBux.Service.Services;
using ScreenBux.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ScreenBux Parental Control Service";
});

// Register services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DeviceIdentityService>();
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddSingleton<GrantService>();
builder.Services.AddSingleton<ProcessKillerService>();
builder.Services.AddSingleton<PowerActionService>();
builder.Services.AddHostedService<PolicyViolationLoggerService>();
builder.Services.AddSingleton<PolicySyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PolicySyncService>());
builder.Services.AddSingleton<DevicePolicySyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DevicePolicySyncService>());
builder.Services.AddHostedService<NamedPipeServerService>();
builder.Services.AddSingleton<ProcessMonitoringService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProcessMonitoringService>());
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddHostedService<AgentWatchdogService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
var app = host;
