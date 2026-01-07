using ScreenBux.Service;
using ScreenBux.Service.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ScreenBux Parental Control Service";
});

// Register services
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddSingleton<ProcessKillerService>();
builder.Services.AddSingleton<NamedPipeServerService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
