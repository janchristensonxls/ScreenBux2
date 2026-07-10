# Progress: 02-upgrade-all-projects

## Summary
Atomic All-at-Once upgrade of all 5 projects from .NET 8 to .NET 10, plus package bumps. No source code changes were required — the full solution builds with 0 errors and 0 warnings after the retarget.

## Changes made

### Target frameworks (5 projects)
| Project | Before | After |
|---|---|---|
| ScreenBux.Shared | net8.0 | **net10.0** |
| ScreenBux.Agent (WPF) | net8.0-windows | **net10.0-windows** |
| ScreenBux.WebClient (Blazor) | net8.0 | **net10.0** |
| ScreenBux.WebServer (ASP.NET Core) | net8.0 | **net10.0** |
| ScreenBux.Service (Worker) | net8.0 | **net10.0** |

### Package updates
| Package | Before | After | Project(s) |
|---|---|---|---|
| Microsoft.AspNetCore.OpenApi | 8.0.22 | **10.0.9** | WebServer |
| Swashbuckle.AspNetCore | 6.6.2 | **10.2.3** | WebServer |
| Microsoft.AspNetCore.SignalR.Client | 8.0.1 | **10.0.9** | WebClient, Service |
| Microsoft.Extensions.Hosting | 8.0.1 | **10.0.9** | Service |
| Microsoft.Extensions.Hosting.WindowsServices | 8.0.1 | **10.0.9** | Service |

**Swashbuckle decision**: Bumped 6.6.2 → 10.2.3 (not left as-is) to align its `Microsoft.OpenApi` dependency with the 2.x brought in by `Microsoft.AspNetCore.OpenApi` 10.0.9, avoiding a diamond dependency conflict. Low risk — WebServer uses only default Swagger config (`AddSwaggerGen`/`UseSwagger`/`UseSwaggerUI`, no custom filters or `Microsoft.OpenApi` code).

## Assessment flags — dispositioned, no code change needed
- **112 × Api.0001** (binary-incompatibility, mostly WPF `DispatcherTimer` in Agent): resolved by recompiling against .NET 10.
- **Api.0002** `TimeSpan.FromSeconds/FromMinutes` (Service: ProcessKillerService, ProcessMonitoringService, PolicySyncService, Worker; Agent: MonitoringService): all call sites pass integer literals; the new `long` overload produces identical results. Compiles clean.
- **Api.0003** `JsonDocument.Parse` (NamedPipeServerService), `HttpContent.ReadAsStringAsync` / `Uri` / `UseExceptionHandler` (WebClient): *Potential* behavioral flags only; no API surface change affecting compilation. No behavioral regression relevant to current usage.

## Build result
- `run_build` (full solution): **Build successful — 0 errors, 0 warnings.**
- One transient failure mid-task: the Service `.csproj` TargetFramework line was initially missed (stale `net8.0` while Shared was already `net10.0`), causing NU1201. Fixed by retargeting Service to net10.0; rebuild clean.

## Files modified
- src/ScreenBux.Shared/ScreenBux.Shared.csproj
- src/ScreenBux.Agent/ScreenBux.Agent.csproj
- src/ScreenBux.WebClient/ScreenBux.WebClient.csproj
- src/ScreenBux.WebServer/ScreenBux.WebServer.csproj
- src/ScreenBux.Service/ScreenBux.Service.csproj

## Done-when verification
- ✅ All 5 .csproj target net10.0 / net10.0-windows.
- ✅ 4 recommended packages updated to 10.0.9 (+ Swashbuckle to 10.2.3 for alignment).
- ✅ Full solution builds with 0 errors and 0 warnings.
- ✅ No new API/behavioral regressions left unaddressed.

## Deferred / notes
- Pre-existing config quirks (hub URL/port mismatch, CORS origin) were **not** touched — out of scope for the upgrade and not broken by it. To be surfaced in Task 03.
