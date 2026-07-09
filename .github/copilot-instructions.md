# ScreenBux2 — AI Agent Guide

Windows **parental-control** system. A privileged service enforces policy on a controlled
device; a web server + Blazor UI let a parent manage policy and watch activity in real time.
Target framework: **.NET 8**. Solution: `ScreenBux2.sln`.

## The five projects (and who talks to whom)

| Project | Type | Responsibility |
|---|---|---|
| `src/ScreenBux.Shared` | classlib (`net8.0`) | DTOs/models, pipe message contracts, `PolicyStorage` path helper. Referenced by everyone. |
| `src/ScreenBux.Service` | Worker / Windows Service (`net8.0`) | The enforcement engine. Scans processes, matches policy, closes apps. Hosts a Named Pipe server. Acts as a **SignalR client** to the web server for policy sync. |
| `src/ScreenBux.Agent` | WPF (`net8.0-windows`) | Desktop app in the user session. Detects the foreground window and reports it to the Service over Named Pipes. |
| `src/ScreenBux.WebServer` | ASP.NET Core Web API + SignalR (`net8.0`) | REST `PolicyController` + `MonitoringHub` at `/monitoringHub`. Reads/writes `policy.json`. |
| `src/ScreenBux.WebClient` | Blazor Server (`net8.0`) | Parent control panel. SignalR client + REST client. |

### Data flow (current reality)
1. **Agent → Service** (Named Pipe `ScreenBuxServicePipe`): `ProcessReportMessage` in, `CloseProcessCommand`/`CommandResponse` back.
2. **Service** also independently enumerates `Process.GetProcesses()` + foreground window every `CheckIntervalSeconds` and enforces directly (see `ProcessMonitoringService`).
3. **WebClient ↔ WebServer**: REST for policy (`api/policy`), SignalR for live events.
4. **WebServer → Service**: `PUT api/policy` writes `policy.json` **and** broadcasts `PolicyUpdated` on the hub; the Service's `PolicySyncService` (a hub client) receives it and calls `PolicyService.UpdatePolicyAsync`.

## Domain model — read this before touching policy code

- `PolicyConfiguration` holds **two parallel rule systems**:
  - `Rules: List<PolicyRule>` — the **primary/intended** model: regex on `ProcessNameRegex` and/or `WindowTitleRegex`. This is the "forbid apps by regex" feature.
  - `Policies: List<AppPolicy>` — a **legacy** model: name/path match + `PolicyAction` (Allow/Block/TimeRestricted) + `AllowedTimeWindows` + `MaxUsageMinutesPerDay`.
- `PolicyService.ShouldBlockProcess` uses **Rules first and returns early if any Rule exists**, so the `AppPolicy` time-window / usage-limit path is effectively **dead whenever `Rules` is non-empty**. Keep this precedence in mind; don't assume `AppPolicy` logic runs.
- `MaxUsageMinutesPerDay` and any notion of a **total daily time budget are declared but never enforced** — there is no usage accumulation anywhere.
- Policy persistence is a **single JSON file** at `PolicyStorage.GetDefaultPolicyPath()` → `%CommonApplicationData%\ScreenBux\policy.json`. WebServer and Service assume they share this path (i.e., **co-located on one machine**). There is no database.

## Conventions

- C#: nullable enabled, implicit usings, file-scoped namespaces, constructor DI, `ILogger<T>` logging with structured templates.
- Async everywhere for I/O; background work uses `BackgroundService`.
- Pipe/SignalR payloads are System.Text.Json; every pipe message implements `Contracts.INamedPipeMessage` with a string `MessageType` discriminator. To add a message type: add a class in `ScreenBux.Shared/Messages`, then handle its `MessageType` in `NamedPipeServerService.ProcessMessageAsync`.
- Windows P/Invoke (`user32.dll`) lives in `ForegroundWindowDetector` — **duplicated** in both Service and Agent. Prefer editing both or consolidating into Shared if you change it.
- Add new Blazor pages under `Components/Pages` and register the link in `Components/Layout/NavMenu.razor`.

## Build & run

```powershell
dotnet build ScreenBux2.sln
# Run order for a full local loop:
dotnet run --project src/ScreenBux.WebServer     # hub + REST
dotnet run --project src/ScreenBux.WebClient     # parent UI
dotnet run --project src/ScreenBux.Service       # enforcement (run elevated to kill processes)
dotnet run --project src/ScreenBux.Agent         # WPF, Windows only
```
There is **no test project** and the `.github/workflows` files are empty — no CI to satisfy yet.

## Known gotchas (don't "fix" by accident; verify intent first)

- **Hub URL / port mismatch**: `PolicySyncService` code default is `https://localhost:7225/monitoringHub`, but every `appsettings*.json` uses `:44323`. WebServer `launchSettings` exposes both (`7225` Kestrel, `44323` IIS Express). Keep config-driven values consistent when editing.
- **CORS** in `WebServer/Program.cs` allows origin `:5173`, which does not match the Blazor client's actual port.
- **Detections are not pushed to the web UI**: `MonitoringHub.BroadcastProcessDetection` exists but nothing calls it from the Service, so the WebClient "Monitoring" page rarely shows live processes. The Service has no `IHubContext`/hub-invoke for detections.
- **Kill-tree is a stub**: `ProcessKillerService.GetChildProcesses` always returns empty; only the parent process is closed.
- **No authentication/authorization** anywhere (API, hub, or pipe), despite README security notes.
- WebClient still contains template pages (`Counter`, `Weather`) — not part of the domain.

## Missing capabilities (major — see `docs/STATUS-AND-ROADMAP.md`)

There is currently **no concept of an account/parent, no device identity, and no device linking**.
Everything is single-machine and single-`policy.json`. Cross-device / aggregated "total time"
enforcement is therefore not yet possible. When implementing these, expect to introduce
server-side persistence (EF Core), identity/auth, a `DeviceId`/`ProfileId` on reports and hub
groups, and a usage ledger — read the roadmap doc before starting.
