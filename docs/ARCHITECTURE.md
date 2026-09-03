# ScreenBux2 — Architecture & Status

> **Purpose of this document:** a single, accurate reference for how the system is
> actually wired together today (not the aspirational design). Written for both
> human contributors and AI coding agents. Update it whenever a data flow,
> project responsibility, or major gap changes.

ScreenBux2 is a Windows **parental-control** system. A privileged Windows Service
enforces policy on the controlled PC; a cloud-style web server + Blazor UI let a
parent manage policy, accounts, and linked devices. Target framework: **.NET 8**
(actual `<TargetFramework>` values in the `.csproj` files — see Projects table).
Solution file: `ScreenBux2.sln`.

## Projects

| Project | Type | Responsibility |
|---|---|---|
| `src/ScreenBux.Shared` | classlib | DTOs/models (`PolicyConfiguration`, `PolicyRule`, `AppPolicy`, `ProcessInfo`, auth/device DTOs), named-pipe message contracts, `PolicyStorage` path helper. Referenced by every other project. |
| `src/ScreenBux.Data` | classlib | EF Core `AppDbContext` (SQL Server) + entities: `Account` (ASP.NET Core Identity user), `ChildProfile`, `Device`, `DeviceLinkCode`, `PolicyDocument`. Owns migrations. Referenced by `ScreenBux.WebServer` only. |
| `src/ScreenBux.Service` | Worker / Windows Service | The enforcement engine running on the controlled PC. Scans processes, matches policy, closes/kills matching processes. Hosts a Named Pipe server for the Agent. Generates/persists a local device identity, redeems a link code to bind to a parent account, and syncs policy from the server (both pull via REST and push via SignalR). |
| `src/ScreenBux.Agent` | WPF (`net8.0-windows`) | Desktop app running in the user's interactive session. Detects the foreground window (P/Invoke on `user32.dll`) and reports it to the Service over Named Pipes. |
| `src/ScreenBux.WebServer` | ASP.NET Core Web API + SignalR | REST controllers (`AccountController`, `DevicesController`, `PolicyController`) + `MonitoringHub` at `/monitoringHub`. Backed by EF Core/SQL Server via `ScreenBux.Data`. Issues JWTs for both parent accounts and devices. |
| `src/ScreenBux.WebClient` | Blazor Server | Parent control panel. Talks to WebServer over REST (via typed API services) and SignalR, using a JWT stored/managed client-side. |

## Data flow (current reality)

1. **Agent → Service** (Named Pipe `ScreenBuxServicePipe`): Agent sends
   `ProcessReportMessage` (with optional `DeviceId`), containing the real
   foreground process **and** its window title as seen in the interactive user
   session; Service replies with `CommandResponse` (or, only as a fallback,
   `CloseProcessCommand`). Message contracts implement
   `Contracts.INamedPipeMessage` (`MessageType` discriminator), defined in
   `ScreenBux.Shared/Messages/NamedPipeMessages.cs`. This is the **only** place
   `WindowTitleRegex` rules are evaluated (`NamedPipeServerService.HandleProcessReportAsync`
   calls `PolicyService.GetMatchingRule(..., isForegroundWindow: true)`),
   because a real Windows Service runs in Session 0 on a non-interactive
   window station and cannot see the interactive user's desktop/windows. The
   Service decides via `PolicyService`, then **enforces directly itself** via
   `ProcessKillerService.TryCloseProcessAsync` (Windows processes are
   machine-global, so the Service can open a handle to the reported PID
   without going through the Agent) — this lets enforcement benefit from
   whatever elevated rights the Service runs with. Only if that Service-side
   attempt fails (e.g. access denied on a protected/admin-launched process)
   does the Service reply with `CloseProcessCommand`, asking the Agent to try
   a best-effort graceful `CloseMainWindow()` in its own session as a fallback.
2. **Service enforcement loop** (`ProcessMonitoringService`, a `BackgroundService`):
   every `CheckIntervalSeconds`, enumerates `Process.GetProcesses()` and matches
   against policy via `PolicyService` using `isForegroundWindow: false` — i.e.
   only `ProcessNameRegex`/legacy name-based matches, never window-title rules.
   This runs independently of what the Agent reports and exists to catch
   name-matched processes even when they're not in the foreground. There is no
   Service-side foreground/window-title detection (a prior
   `ForegroundWindowDetector` in this project was removed as non-functional
   dead code — Session 0 services cannot call `GetForegroundWindow` against the
   interactive desktop).
3. **Device identity & linking**:
   - `DeviceIdentityService` generates a random `DeviceId` + `MachineKey` on
	 first run and persists them to `device.json` next to the local policy
	 cache (`%CommonApplicationData%\ScreenBux\`).
   - A parent generates a short-lived link code via
	 `POST /api/devices/linkcode` (requires a parent JWT).
   - The Service (`DevicePolicySyncService`, a `BackgroundService`) redeems the
	 code via `POST /api/devices/redeem` (configured `LinkCode` app setting),
	 creating/updating a `Device` row and receiving a **device-scoped JWT**,
	 which it persists in `device.json`.
   - Once linked, `PolicySyncService` opens a SignalR connection to
	 `/monitoringHub`, authenticating with the device token (sent via
	 `access_token` query string, since browsers/SignalR can't set the
	 `Authorization` header on the WS handshake — same mechanism the
	 WebClient uses).
4. **Policy sync to the Service** (two parallel paths):
   - **Pull**: `DevicePolicySyncService` periodically calls
	 `GET /api/devices/{id}/policy` and writes the result to the local
	 `policy.json` consumed by `PolicyService`.
   - **Push**: `PolicyController.UpdatePolicy` (`PUT /api/policy`) saves the
	 new policy to `PolicyDocument` and broadcasts `PolicyUpdated` to the
	 account's SignalR group; `PolicySyncService` (hub client) receives it and
	 calls `PolicyService.UpdatePolicyAsync` directly, bypassing the file's
	 next scheduled pull.
5. **WebClient ↔ WebServer**: REST for accounts/devices/policy (JWT bearer,
   obtained from `AccountController.Login`/`Register`), SignalR for live
   events. `MonitoringHub` groups connections by `accountId` so a parent only
   sees their own account's device traffic.

## Domain model — read this before touching policy code

### Identity & multi-tenancy (implemented)
- `Account : IdentityUser` — the parent's login (ASP.NET Core Identity, email +
  password). One account can have many `ChildProfile`s and many `Device`s.
- `ChildProfile` — a person whose time budget is *intended* to span multiple
  devices (see "Not yet enforced" below — nothing currently aggregates by
  child across devices).
- `Device` — a controlled PC, keyed by a server-issued `Id` (Guid) and a
  client-generated stable `MachineKey` (unique index). Linked to exactly one
  `Account`, optionally one `ChildProfile`.
- `DeviceLinkCode` — a short (8-char, ambiguity-free alphabet), 15-minute-lived
  code a parent generates and a device redeems once. One-time use
  (`RedeemedAt`/`RedeemedByDeviceId`).
- `PolicyDocument` — a policy scoped to `AccountId` + optionally
  `ChildProfileId`/`DeviceId`, storing serialized `PolicyConfiguration` JSON.
  `EfPolicyStore` currently only reads/writes the **account-level, unscoped**
  document (`ChildProfileId == null && DeviceId == null`) from
  `GetPolicyAsync`/`SavePolicyAsync`; `GetDevicePolicyAsync` looks for a
  device-specific document first (falls back — see file for the rest of the
  method) but nothing in the UI currently creates per-device or per-child
  policy documents.

### Policy matching — two parallel rule systems (legacy debt)
`PolicyConfiguration` still holds **two** independent rule systems:
- `Rules: List<PolicyRule>` — the **primary/intended** model: regex on
  `ProcessNameRegex` and/or `WindowTitleRegex`. This is the "forbid apps by
  regex" feature and is what the UI and defaults use.
- `Policies: List<AppPolicy>` — a **legacy** model: name/path match +
  `PolicyAction` (Allow/Block/TimeRestricted) + `AllowedTimeWindows` +
  `MaxUsageMinutesPerDay`.

`PolicyService.ShouldBlockProcess` checks `Rules` first and **returns as soon
as any `Rule` matches**, and `ProcessMonitoringService.EnforcePoliciesAsync`
only bothers resolving `Process.MainModule`/`ExecutablePath` when **no**
`Rules` are enabled (`Policies.Count > 0`) — resolving it otherwise causes a
flood of `Win32Exception`s for protected/system processes. Net effect: the
`AppPolicy` time-window / usage-limit path is **effectively dead whenever any
`Rule` exists**. Don't assume `AppPolicy` logic runs unless you've confirmed
`Rules` is empty for that policy document.

`MaxUsageMinutesPerDay` and any notion of a **total daily time budget across
devices are declared but never enforced** — there is no usage accumulation
anywhere in the codebase.

### Persistence split (important — two different stores for policy)
- **`ScreenBux.Service`** still reads/writes policy as a **flat JSON file** at
  `PolicyStorage.GetDefaultPolicyPath()` (`%CommonApplicationData%\ScreenBux\policy.json`)
  via `PolicyService`. This is the file the enforcement loop actually consults.
- **`ScreenBux.WebServer`** persists policy in **SQL Server** via
  `EfPolicyStore`/`PolicyDocument` (EF Core, `ScreenBux.Data`). On first
  read for an account with no `PolicyDocument`, `EfPolicyStore` seeds itself
  from the **local legacy `policy.json` on the server's own machine**
  (`LoadLegacyPolicyOrDefault`) — this only makes sense if the WebServer and a
  Service happen to be co-located; it is not a real per-account seed. This is
  a remnant of the pre-accounts single-machine design and should not be
  trusted for a real deployment.
- The Service never talks to SQL Server directly; it only ever sees policy via
  REST (`DevicePolicySyncService`) or SignalR (`PolicySyncService`), then
  writes to its own local `policy.json`.

## Conventions

- C#: nullable enabled, implicit usings, file-scoped namespaces, constructor
  DI, `ILogger<T>` logging with structured templates.
- Async everywhere for I/O; background work uses `BackgroundService`
  (`ProcessMonitoringService`, `PolicySyncService`, `DevicePolicySyncService`,
  `NamedPipeServerService`).
- Pipe/SignalR payloads are System.Text.Json; every pipe message implements
  `Contracts.INamedPipeMessage` with a string `MessageType` discriminator. To
  add a message type: add a class in `ScreenBux.Shared/Messages`, then handle
  its `MessageType` in `NamedPipeServerService.ProcessMessageAsync`.
- Windows P/Invoke (`user32.dll`) lives in `ForegroundWindowDetector` —
  **duplicated** in both `ScreenBux.Service` and `ScreenBux.Agent`. Prefer
  editing both, or consolidate into `ScreenBux.Shared`, if you change it.
- JWT auth: `JwtTokenService` issues two distinct token shapes — an
  **account token** (parent login, has `accountId` claim, checked via
  `User.GetAccountId()`) and a **device token** (has `deviceId` +`accountId`
  claims, checked via `User.GetDeviceId()`). `DevicesController.GetDevicePolicy`
  is the one endpoint that accepts either and enforces "a device token may
  only read its own policy."
- Add new Blazor pages under `Components/Pages` and register the link in
  `Components/Layout/NavMenu.razor`.
- EF Core migrations live in `ScreenBux.Data/Migrations`; `AppDbContextFactory`
  supports design-time `dotnet ef` commands. New entities/columns require a
  new migration (`dotnet ef migrations add <Name> -p src/ScreenBux.Data -s src/ScreenBux.WebServer`).

## Build & run

```powershell
dotnet build ScreenBux2.sln
# Run order for a full local loop:
dotnet run --project src/ScreenBux.WebServer     # REST API + hub + SQL Server (needs ConnectionStrings:AppDb + Jwt:SigningKey configured, e.g. via user-secrets)
dotnet run --project src/ScreenBux.WebClient     # parent UI
dotnet run --project src/ScreenBux.Service       # enforcement (run elevated to kill processes); needs ServerBaseUrl + a LinkCode to link on first run
dotnet run --project src/ScreenBux.Agent         # WPF, Windows only
```

There is **no test project** in the solution and the `.github/workflows`
files are empty — no CI to satisfy yet.

## Known gotchas (don't "fix" by accident; verify intent first)

- **Two rule systems, one wins silently** — see "Policy matching" above.
  Adding features to `AppPolicy`/time-window enforcement will have no visible
  effect unless `Rules` is also empty for that policy.
- **Two policy stores** — the WebServer's SQL-backed `PolicyDocument` and the
  Service's local `policy.json` are bridged only through
  REST pull / SignalR push. If you change the `PolicyConfiguration` shape,
  update both serialization paths and consider migration/back-compat for
  already-linked devices' cached `policy.json`.
- **Legacy-seed heuristic in `EfPolicyStore`** reads a local file on the
  WebServer's own machine as a fallback default — likely wrong/unused in any
  real (non-co-located) deployment; don't assume it does per-account seeding.
- **Per-device/per-child policy documents are mostly unused**: `PolicyDocument`
  supports `ChildProfileId`/`DeviceId` scoping and `GetDevicePolicyAsync` reads
  it, but nothing in `PolicyController`/WebClient currently *writes* a scoped
  document — every parent edit goes to the single unscoped, account-wide
  document.
- **Kill-tree is a stub**: `ProcessKillerService.GetChildProcesses` (see full
  file) may not return real child processes — verify current behavior before
  relying on it for nested/launcher-spawned games or browsers.
- **CORS in `WebServer/Program.cs`** is hardcoded to a fixed list of
  localhost ports for `dotnet run`/IIS Express profiles — check
  `Properties/launchSettings.json` in both `WebServer` and `WebClient` before
  assuming a given port is (or isn't) allowed.
- **No usage-ledger / cross-device time budget**: `MaxUsageMinutesPerDay` and
  `ChildProfile` spanning multiple devices exist as *shape* but nothing
  accumulates usage anywhere (Service, WebServer, or Data). Implementing this
  requires a new usage-tracking table/service, not just wiring up the
  existing fields.
- **`MonitoringHub.BroadcastProcessDetection`** exists and is callable by
  clients, but check whether the Service actually invokes an
  equivalent server-to-clients broadcast for live "process detected" events
  before assuming the WebClient monitoring page is fully wired end-to-end.

## Suggested next steps for contributors / agents

When asked to add a capability, check this doc first for whether the
underlying plumbing already exists (accounts, devices, JWTs, EF Core are all
real now) before assuming you need to build auth/identity from scratch. The
biggest genuinely-missing piece is **usage tracking/enforcement** — everything
else (multi-tenant accounts, device linking, per-account policy) has a working
first version.
