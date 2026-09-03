# ScreenBux2 — AI Agent Guide

Windows **parental-control** system. A privileged service enforces policy on a controlled
device; a web server + Blazor UI let a parent manage accounts, linked devices, and policy,
and watch activity in real time. Target framework: **.NET 8**. Solution: `ScreenBux2.sln`.

> For full architecture detail (data flow, domain model, known gaps), see
> [`docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md). Keep that file in sync when you change
> data flow, add a project, or close/discover a gap — this file stays short on purpose.

## The six projects (and who talks to whom)

| Project | Type | Responsibility |
|---|---|---|
| `src/ScreenBux.Shared` | classlib (`net8.0`) | DTOs/models, pipe message contracts, `PolicyStorage` path helper. Referenced by everyone. |
| `src/ScreenBux.Data` | classlib (`net8.0`) | EF Core `AppDbContext` (SQL Server) + entities: `Account` (ASP.NET Core Identity), `ChildProfile`, `Device`, `DeviceLinkCode`, `PolicyDocument`, plus migrations. Referenced only by `ScreenBux.WebServer`. |
| `src/ScreenBux.Service` | Worker / Windows Service (`net8.0`) | The enforcement engine. Scans processes, matches policy, closes apps. Hosts a Named Pipe server. Generates a local device identity, redeems a link code to bind to an account, and syncs policy via REST pull + SignalR push. |
| `src/ScreenBux.Agent` | WPF (`net8.0-windows`) | Desktop app in the user session. Detects the foreground window and reports it to the Service over Named Pipes. |
| `src/ScreenBux.WebServer` | ASP.NET Core Web API + SignalR (`net8.0`) | REST `AccountController` (register/login, JWT), `DevicesController` (link codes, device tokens, per-device policy), `PolicyController` (account policy CRUD) + `MonitoringHub` at `/monitoringHub`. Backed by SQL Server via `ScreenBux.Data`. |
| `src/ScreenBux.WebClient` | Blazor Server (`net8.0`) | Parent control panel. JWT-based auth, REST + SignalR client. |

### Data flow (current reality)
1. **Agent → Service** (Named Pipe `ScreenBuxServicePipe`): `ProcessReportMessage` in, `CloseProcessCommand`/`CommandResponse` back.
2. **Service** also independently enumerates `Process.GetProcesses()` + foreground window every `CheckIntervalSeconds` and enforces directly (see `ProcessMonitoringService`).
3. **Device linking**: parent generates a link code (`POST api/devices/linkcode`, JWT-authed); the Service redeems it (`POST api/devices/redeem`) using a locally-generated, persisted `DeviceId`/`MachineKey` (`DeviceIdentityService`), receiving a device-scoped JWT it stores in `device.json` alongside the local policy cache.
4. **Policy sync to the Service** is two parallel paths: `DevicePolicySyncService` polls `GET api/devices/{id}/policy`, and `PolicySyncService` (a SignalR hub client, authenticated with the device token) receives a live `PolicyUpdated` push whenever `PolicyController.UpdatePolicy` (`PUT api/policy`) saves a change. Both write into the Service's own local `policy.json` — the Service itself never touches SQL Server.
5. **WebClient ↔ WebServer**: REST for accounts/devices/policy (JWT bearer), SignalR for live events. `MonitoringHub` groups connections by `accountId`.

## Domain model — read this before touching policy code

- Multi-tenancy is real now: `Account : IdentityUser` owns many `ChildProfile`s and `Device`s (`ScreenBux.Data`, SQL Server via EF Core). `DeviceLinkCode` is a one-time, 15-minute code binding a `Device` to an `Account`.
- `PolicyConfiguration` still holds **two parallel rule systems**:
  - `Rules: List<PolicyRule>` — the **primary/intended** model: regex on `ProcessNameRegex` and/or `WindowTitleRegex`. This is the "forbid apps by regex" feature.
  - `Policies: List<AppPolicy>` — a **legacy** model: name/path match + `PolicyAction` (Allow/Block/TimeRestricted) + `AllowedTimeWindows` + `MaxUsageMinutesPerDay`.
- `PolicyService.ShouldBlockProcess` uses **Rules first and returns early if any Rule exists**, so the `AppPolicy` time-window / usage-limit path is effectively **dead whenever `Rules` is non-empty**. Keep this precedence in mind; don't assume `AppPolicy` logic runs.
- `MaxUsageMinutesPerDay` and any notion of a **total daily time budget across devices are declared but never enforced** — there is no usage accumulation anywhere, despite `ChildProfile` now existing to model a person spanning multiple devices.
- Policy has **two separate stores** that are only bridged via REST/SignalR: the WebServer persists `PolicyConfiguration` as JSON inside a SQL Server `PolicyDocument` row (`EfPolicyStore`, scoped by `AccountId`/optionally `ChildProfileId`/`DeviceId` — though only the unscoped account-wide document is actually written today); the Service persists its own local flat-file copy at `PolicyStorage.GetDefaultPolicyPath()` (`%CommonApplicationData%\ScreenBux\policy.json`). Don't assume either side can see the other's storage directly.

## Conventions

- C#: nullable enabled, implicit usings, file-scoped namespaces, constructor DI, `ILogger<T>` logging with structured templates.
- Async everywhere for I/O; background work uses `BackgroundService`.
- Pipe/SignalR payloads are System.Text.Json; every pipe message implements `Contracts.INamedPipeMessage` with a string `MessageType` discriminator. To add a message type: add a class in `ScreenBux.Shared/Messages`, then handle its `MessageType` in `NamedPipeServerService.ProcessMessageAsync`.
- Windows P/Invoke (`user32.dll`) lives in `ForegroundWindowDetector` — **duplicated** in both Service and Agent. Prefer editing both or consolidating into Shared if you change it.
- JWT auth issues two distinct token shapes from `JwtTokenService`: an **account token** (`User.GetAccountId()`) for parents, and a **device token** (`User.GetDeviceId()` + account) for the Service. Endpoints that accept device tokens must explicitly check the caller only accesses its own device (see `DevicesController.GetDevicePolicy`).
- Add new Blazor pages under `Components/Pages` and register the link in `Components/Layout/NavMenu.razor`.
- EF Core migrations live in `ScreenBux.Data/Migrations`; add new ones with `dotnet ef migrations add <Name> -p src/ScreenBux.Data -s src/ScreenBux.WebServer` whenever an entity changes.

## Build & run

```powershell
dotnet build ScreenBux2.sln
# Run order for a full local loop:
dotnet run --project src/ScreenBux.WebServer     # REST API + hub, needs SQL Server connection string + Jwt:SigningKey (user-secrets)
dotnet run --project src/ScreenBux.WebClient     # parent UI
dotnet run --project src/ScreenBux.Service       # enforcement (run elevated to kill processes); needs ServerBaseUrl + a LinkCode to link on first run
dotnet run --project src/ScreenBux.Agent         # WPF, Windows only
```
There is **no test project** and the `.github/workflows` files are empty — no CI to satisfy yet.

## Known gotchas (don't "fix" by accident; verify intent first)

- **Two rule systems, one wins silently** — see "Domain model" above.
- **Two policy stores, bridged only via network** — SQL Server (WebServer) vs. local `policy.json` (Service). Changing `PolicyConfiguration`'s shape means updating both serialization paths.
- **`EfPolicyStore` legacy-seed fallback** reads a local `policy.json` on the WebServer's own machine as a default when an account has no `PolicyDocument` yet — a holdover from the single-machine design; don't treat it as real per-account seeding logic.
- **Per-device/per-child policy documents are mostly unused**: the schema supports scoping by `ChildProfileId`/`DeviceId`, but nothing currently writes a scoped document — all parent edits go to one account-wide document.
- **Kill-tree correctness**: verify `ProcessKillerService`'s child-process enumeration actually returns children before relying on it for nested/launcher-spawned processes.
- **CORS** in `WebServer/Program.cs` is a fixed allow-list of localhost ports — check `launchSettings.json` in both WebServer and WebClient before assuming a port is (or isn't) allowed.
- WebClient still contains template pages, if any remain — check `Components/Pages` for domain-specific pages only (`Home`, `Login`, `LinkDevice`, `Monitoring`, `Policy`) before adding new ones.

## Missing capabilities (major — see [`docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md))

The biggest genuinely-missing piece is **usage tracking/enforcement**: `MaxUsageMinutesPerDay`
and multi-device `ChildProfile` budgets exist as shape only — nothing accumulates usage
anywhere. Accounts, device identity/linking, and JWT auth are already implemented; don't
rebuild them from scratch.
