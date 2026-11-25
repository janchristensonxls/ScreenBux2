# Database Migrations

The database schema is managed with EF Core migrations.

| What | Where |
|---|---|
| `AppDbContext`, entities, migrations | `src/ScreenBux.Data` (the **migrations project**) |
| Connection string (`ConnectionStrings:AppDb`) | `src/ScreenBux.WebServer` - `appsettings.json`, `appsettings.{Environment}.json`, user secrets (the **startup project**) |

`AppDbContextFactory` (design-time) reads the connection string from the WebServer's settings,
so the EF tools must always be pointed at **both** projects. The environment defaults to
`Development` unless `ASPNETCORE_ENVIRONMENT` is set.

## Apply migrations (update the database)

### Visual Studio - Package Manager Console

The current folder doesn't matter; the project selection does.

```powershell
Update-Database -Project ScreenBux.Data -StartupProject ScreenBux.WebServer
```

(Alternatively set the console's **Default project** dropdown to `ScreenBux.Data` and the
solution's startup project to `ScreenBux.WebServer`, then run plain `Update-Database`.)

### Command line (`dotnet ef`)

Run from the repository root:

```powershell
dotnet ef database update -p src/ScreenBux.Data -s src/ScreenBux.WebServer
```

Install the tool once if needed: `dotnet tool install --global dotnet-ef`.

## Add a migration

After changing an entity or `AppDbContext`:

```powershell
Add-Migration <Name> -Project ScreenBux.Data -StartupProject ScreenBux.WebServer
```

```powershell
dotnet ef migrations add <Name> -p src/ScreenBux.Data -s src/ScreenBux.WebServer
```

Review the generated `Up`/`Down` code in `src/ScreenBux.Data/Migrations` before committing, and
commit the migration, its `.Designer.cs` and the updated `AppDbContextModelSnapshot.cs` together.

## Other useful commands

| Purpose | Package Manager Console | `dotnet ef` (from repo root) |
|---|---|---|
| List migrations and which are applied | `Get-Migration -Project ScreenBux.Data -StartupProject ScreenBux.WebServer` | `dotnet ef migrations list -p src/ScreenBux.Data -s src/ScreenBux.WebServer` |
| Remove the last, **unapplied** migration | `Remove-Migration -Project ScreenBux.Data -StartupProject ScreenBux.WebServer` | `dotnet ef migrations remove -p src/ScreenBux.Data -s src/ScreenBux.WebServer` |
| Roll back to an earlier migration | `Update-Database <Name> -Project ScreenBux.Data -StartupProject ScreenBux.WebServer` | `dotnet ef database update <Name> -p src/ScreenBux.Data -s src/ScreenBux.WebServer` |
| SQL script for a production deploy | `Script-Migration -Idempotent -Project ScreenBux.Data -StartupProject ScreenBux.WebServer` | `dotnet ef migrations script --idempotent -p src/ScreenBux.Data -s src/ScreenBux.WebServer` |

## Troubleshooting

- **"The term 'Update-Database' is not recognized"** (Package Manager Console) - the PMC
  commands come from the `Microsoft.EntityFrameworkCore.Tools` package, referenced by
  `ScreenBux.Data`. Restore/build the solution, then close and reopen the solution (or
  Visual Studio) so the console loads the module. Or use the `dotnet ef` commands instead.

- **"Connection string 'AppDb' was not found"** - the startup project isn't
  `ScreenBux.WebServer`, or the connection string is missing from its settings/user secrets.
- **"No DbContext was found" / "Your startup project doesn't reference ..."** - the
  migrations project isn't `ScreenBux.Data`.
- **Wrong database updated** - check `ASPNETCORE_ENVIRONMENT`; it selects which
  `appsettings.{Environment}.json` is read.
