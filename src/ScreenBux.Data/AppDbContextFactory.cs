using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ScreenBux.Data;

/// <summary>
/// Design-time factory so EF Core tools can create the context when running
/// migrations against this class library (which has no host of its own).
/// The runtime connection string is supplied by the WebServer host at startup;
/// this value is only used by <c>dotnet ef</c> at design time.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=ScreenBux;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=False");

        return new AppDbContext(optionsBuilder.Options);
    }
}
