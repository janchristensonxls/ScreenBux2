using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ScreenBux.Data;

/// <summary>
/// Design-time factory so EF Core tools can create the context when running
/// migrations against this class library (which has no host of its own).
/// Reads the same appsettings.json/appsettings.{Environment}.json + user-secrets
/// that the WebServer host uses at runtime, so `dotnet ef` targets whatever
/// database the WebServer is actually configured to use - no more hardcoded
/// connection strings that silently diverge from appsettings.json.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // "src/ScreenBux.Data" -> "src/ScreenBux.WebServer", so this works whether `dotnet ef`
        // is invoked with -s src/ScreenBux.WebServer from the repo root or from within this project.
        var webServerBasePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "ScreenBux.WebServer");
        if (!File.Exists(Path.Combine(webServerBasePath, "appsettings.json")))
        {
            webServerBasePath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(webServerBasePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddUserSecrets("dotnet-ScreenBux.WebServer-1e3c6f2a-9b7d-4a5e-8c1f-2d6a9b3e7f01")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                $"Connection string 'AppDb' was not found under {webServerBasePath}. " +
                "Set it in ScreenBux.WebServer/appsettings.Development.json or via `dotnet user-secrets`.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
