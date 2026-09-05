using ScreenBux.Updater.Services;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows Service, always installed to run elevated (LocalSystem), so it can stop
// the ScreenBux Service, replace its files, restart it, and relaunch the Agent in the active
// user session after an update - none of which the Service or Agent can safely do to
// themselves while running.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ScreenBux Updater";
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SessionLauncher>();
builder.Services.AddSingleton<ServiceUpdater>();
builder.Services.AddSingleton<AgentUpdater>();

builder.Services.AddHostedService<UpdateCheckService>();

var host = builder.Build();
host.Run();
