using ScreenBux.Updater.Services;

// Invoked directly (not via the Windows Service Control Manager) by installers/uninstallers
// to remove the Service and Agent that this Updater previously provisioned. An ordinary
// MSI/PowerShell uninstaller for the Updater has no idea those components exist or how to
// remove them - only the Updater itself, which installed them via ServiceInstaller/
// AgentUpdater in the first place, knows how. Runs as a one-shot console invocation and exits
// with a non-zero code on failure so the calling installer can detect it.
if (args.Contains("--cleanup-managed-components"))
{
    return await RunCleanupAsync(args);
}

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
return 0;

static async Task<int> RunCleanupAsync(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    var serviceUpdaterLogger = loggerFactory.CreateLogger<ServiceUpdater>();
    var agentUpdaterLogger = loggerFactory.CreateLogger<AgentUpdater>();
    var sessionLoggerFactory = loggerFactory.CreateLogger<SessionLauncher>();

    var serviceUpdater = new ServiceUpdater(serviceUpdaterLogger);
    var agentUpdater = new AgentUpdater(agentUpdaterLogger, new SessionLauncher(sessionLoggerFactory));

    var serviceRemoved = serviceUpdater.RemoveManagedService(configuration["Service:InstallDirectory"]);
    var agentRemoved = agentUpdater.RemoveManagedAgent(configuration["Agent:InstallDirectory"]);

    return serviceRemoved && agentRemoved ? 0 : 1;
}
