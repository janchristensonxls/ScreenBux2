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
| `src/ScreenBux.Data` | classlib | EF Core `AppDbContext` (SQL Server) + entities: `Account` (ASP.NET Core Identity user), `ChildProfile`, `Device`, `DeviceLinkCode`, `PolicyDocument`, `PolicyProfile`. Owns migrations. Referenced by `ScreenBux.WebServer` only. |
| `src/ScreenBux.Service` | Worker / Windows Service | The enforcement engine running on the controlled PC. Scans processes, matches policy, closes/kills matching processes. Hosts a Named Pipe server for the Agent. Generates/persists a local device identity, redeems a link code to bind to a parent account, and syncs policy from the server (both pull via REST and push via SignalR). Also runs `AgentWatchdogService`, which relaunches the Agent into the active console session if it isn't running (see "Agent watchdog" note under Data flow item 1). |
| `src/ScreenBux.Agent` | WPF (`net8.0-windows`) | Desktop app running in the user's interactive session. Detects the foreground window (P/Invoke on `user32.dll`) and reports it to the Service over Named Pipes. |
| `src/ScreenBux.WebServer` | ASP.NET Core Web API + SignalR | REST controllers (`AccountController`, `DevicesController`, `PolicyController`) + `MonitoringHub` at `/monitoringHub`. Backed by EF Core/SQL Server via `ScreenBux.Data`. Issues JWTs for both parent accounts and devices. |
| `src/ScreenBux.WebClient` | Blazor Server | Parent control panel. Talks to WebServer over REST (via typed API services) and SignalR, using a JWT stored/managed client-side. |
| `src/ScreenBux.Updater` | Worker / Windows Service | Always-elevated auto-updater for the Service and Agent (see "Auto-update" section below). Polls the WebServer's update manifest, downloads packages, and drives file replacement/relaunch for both components since neither can safely update itself while running. |

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
   **Agent watchdog**: because window-title enforcement depends entirely on the
   Agent being alive and reporting, a user simply ending the Agent process
   (Task Manager, `taskkill`, etc.) would otherwise blind that enforcement path
   silently. `AgentWatchdogService` (in `ScreenBux.Service`) periodically checks
   for a running `ScreenBux.Agent` process and, if none is found, relaunches
   `Agent:ExecutablePath` into the active console session via the shared
   `SessionLauncher` (`ScreenBux.Shared.Services`, the same
   WTSQueryUserToken/CreateProcessAsUser technique `ScreenBux.Updater` uses to
   relaunch the Agent after an update) — a no-op if there's no interactive
   session yet. This is a mitigation, not a hard guarantee (e.g. deleting the
   Agent's executable would defeat it).
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
- `PolicyDocument` — a pure **pointer**, scoped to `AccountId` + optionally
  `ChildProfileId`/`DeviceId`, that records which `PolicyProfile` is currently
  "active" for that scope via `ActivePolicyProfileId`. It stores **no policy
  JSON of its own** — `EfPolicyStore` currently only reads/writes the
  **account-level, unscoped** document (`ChildProfileId == null && DeviceId ==
  null`) from `GetPolicyAsync`/`SavePolicyAsync`; `GetDevicePolicyAsync` looks
  for a device-specific document first (falls back — see file for the rest of
  the method) but nothing in the UI currently creates per-device or per-child
  policy documents.
- `PolicyProfile` — a **named, reusable policy variant** ("mode") a parent
  authors once and can switch between, e.g. "Normal", "School", "Open",
  "Sleep". Scoped to `AccountId` (optionally `ChildProfileId`), stores its own
  serialized `PolicyConfiguration` JSON. This is the **single source of
  truth** for policy content — `PolicyDocument` never holds a copy of it.
  Selecting a profile as active (`POST /api/policy/profiles/{id}/activate`)
  simply repoints the account's `PolicyDocument.ActivePolicyProfileId` at it
  and broadcasts `PolicyUpdated` with the profile's deserialized content, so
  the Service/Agent runtime path is completely unaware profiles exist — it
  only ever receives/caches the effective `PolicyConfiguration`, same as
  before this feature existed. The raw "Save policy" JSON editor
  (`PUT /api/policy`) edits the content of whichever profile is currently
  active (`EfPolicyStore.GetOrCreateActiveProfileAsync`), so it can never
  drift from the profile system. New accounts get four built-in profiles
  seeded on first access to `GET /api/policy/profiles` or `GET /api/policy`
  (`EfPolicyStore.SeedDefaultProfilesAsync`): "Normal", "Open" (a normal,
  permissive default — not a special/dangerous rule set), "School", and
  "Sleep" (a single `Always`-condition rule with `Action = Sleep`, the
  built-in strict-lockout mode). There is currently no scheduling — mode
  switches are manual only (parent clicks a mode button in `Policy.razor`).

### Policy matching — two parallel rule systems (legacy debt)
`PolicyConfiguration` still holds **two** independent rule systems:
- `Rules: List<PolicyRule>` — the **primary/intended** model: regex on
  `ProcessNameRegex` and/or `WindowTitleRegex`. This is the "forbid apps by
  regex" feature and is what the UI and defaults use. Each rule also carries a
  `ConditionKind` (`ProcessMatch` — the default, evaluated per detected
  process; or `Always` — evaluated once per policy tick, independent of any
  process, used for whole-session actions) and an `Action`
  (`CloseProcess` — the default/original implicit behavior; `KillProcessTree`;
  `Sleep`; `Hibernate`). `Always`-condition rules are handled separately by
  `ProcessMonitoringService.EnforceAlwaysRules`, gated on
  `PolicyService.HasSyncedSinceStartup` (see Sleep/Hibernate note below) —
  they short-circuit the rest of that tick's per-process enforcement since the
  device is about to suspend.
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

### Device time grants (temporary enforcement pause)

Independent of the mode/profile system above, a parent can grant a device a
temporary "bonus time" window that pauses **all** enforcement (regular
`Rules`, the legacy `AppPolicy` path, and `Always`-condition power actions
alike) until an absolute expiry, regardless of whichever policy profile is
currently active. This is modeled as a single `DeviceGrant` row per device
(`ScreenBux.Data.Entities.DeviceGrant`, unique on `DeviceId`) holding only an
`ExpiresAtUtc` timestamp — "is a grant active" is always the stateless check
`ExpiresAtUtc > DateTime.UtcNow`; there is no separate flag or decrementing
counter to keep in sync.

- **WebServer** (`IGrantStore`/`EfGrantStore`) is the sole authoritative
  writer. `DevicesController` exposes `GET/PUT api/devices/{id}/grant` and
  `POST api/devices/{id}/grant/add` (relative minute adjustment, clamped to
  never go negative), and broadcasts a `GrantUpdated` SignalR event to the
  account's `MonitoringHub` group on every write — the same pattern
  `PolicyController` uses for `PolicyUpdated`.
- **Service** (`GrantService`) caches the expiry locally in `grant.json`
  (next to `policy.json`), kept in sync the same two ways policy is:
  `DevicePolicySyncService` polls `GET .../grant` each cycle, and
  `PolicySyncService` listens for the `GrantUpdated` SignalR push.
  `ProcessMonitoringService.ExecuteAsync` checks `GrantService.IsGrantActive`
  before running `EnforceAlwaysRules`/`EnforcePoliciesAsync`, and
  `NamedPipeServerService.HandleProcessReportAsync` does the same before
  acting on an Agent-reported foreground process — so the grant keeps being
  honored purely from the local cache even while disconnected from the
  server, exactly like the existing policy cache.
- **Agent** can ask the Service for the live status via a
  `GrantStatusRequest`/`GrantStatusResponse` named-pipe message pair, and
  renders a countdown in `MainWindow`.
- **WebClient** (`GrantApiService`) reads/writes grants over REST and also
  listens for the `GrantUpdated` SignalR event; `LinkDevice.razor` shows a
  live per-device countdown (refreshed every 5 seconds) with quick
  add/clear controls.

### Persistence split (important — two different stores for policy)

  `PolicyStorage.GetDefaultPolicyPath()` (`%CommonApplicationData%\ScreenBux\policy.json`)
  via `PolicyService`. This is the file the enforcement loop actually consults.
- **`ScreenBux.WebServer`** persists policy in **SQL Server** via
  `EfPolicyStore`/`PolicyDocument`/`PolicyProfile` (EF Core, `ScreenBux.Data`).
  On first read for an account with no active profile, `EfPolicyStore` seeds a
  default "Normal" `PolicyProfile` from the **local legacy `policy.json` on
  the server's own machine** (`LoadLegacyPolicyOrDefault`) — this only makes
  sense if the WebServer and a Service happen to be co-located; it is not a
  real per-account seed. This is a remnant of the pre-accounts single-machine
  design and should not be trusted for a real deployment.
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
- **Two policy stores** — the WebServer's SQL-backed `PolicyProfile`/`PolicyDocument`
  and the Service's local `policy.json` are bridged only through REST pull /
  SignalR push. If you change the `PolicyConfiguration` shape, update both
  serialization paths and consider migration/back-compat for already-linked
  devices' cached `policy.json`. (The previous in-process gap — where the raw
  `PUT api/policy` editor wrote a separate `PolicyDocument.PolicyJson` cache
  that could drift from the active `PolicyProfile` — has been fixed:
  `PolicyDocument` is now a pure pointer with no JSON of its own; all reads and
  writes resolve through the single active `PolicyProfile` row via
  `EfPolicyStore.GetOrCreateActiveProfileAsync`.)
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

## Auto-update

A dedicated `src/ScreenBux.Updater` Windows Service (installed to always run
elevated, e.g. LocalSystem) owns updating **both** the Service and the Agent,
rather than either component updating itself:

- **Manifest**: `UpdatesController` (`GET api/updates/latest`, anonymous) on
  the WebServer returns an `UpdateManifestDto` (`ScreenBux.Shared/Models/Updates`)
  with a `ComponentUpdateInfo` (version, download URL, optional SHA-256) for
  each of `Service` and `Agent`. Backed today by `StaticUpdateManifestStore`,
  a placeholder `IUpdateManifestStore` implementation reading the `Updates`
  section of `appsettings.json` — swap in a release-feed/CI/database-backed
  store later without touching the controller or DTOs.
- **Polling**: `UpdateCheckService` (a `BackgroundService` in
  `ScreenBux.Updater`) polls the manifest on `CheckIntervalMinutes`, compares
  against a locally-tracked installed version file per component (there is no
  version registry on the server side — only this machine knows what's
  actually on disk), downloads the update zip when newer, and applies it.
- **Applying an update**:
  - `ServiceUpdater` first checks whether "ScreenBux Parental Control Service"
    is registered with the SCM (`ServiceController.GetServices()`). If not
    (fresh machine / first run), it extracts the package to
    `Service:InstallDirectory` and **registers the service itself** via a
    `ServiceInstaller` P/Invoke wrapper around `OpenSCManager`/`CreateService`
    (`System.ServiceProcess.ServiceController` can observe/control existing
    services but has no API to create one), pointing it at
    `Service:ExecutablePath`, then starts it. If the service already exists,
    it instead stops it, extracts the zip over the install directory, and
    restarts it — the normal update path.
  - `AgentUpdater` finds any running `ScreenBux.Agent` process (there may be
    none yet on a fresh install), asks it to close gracefully
    (`CloseMainWindow` with a timeout, falling back to `Kill`), extracts the
    zip over the install directory, then relaunches the Agent via
    `SessionLauncher` either way — this path needs no first-install branch
    since the Agent isn't a registered service.
  - `SessionLauncher` P/Invokes `WTSGetActiveConsoleSessionId` /
    `WTSQueryUserToken` / `DuplicateTokenEx` / `CreateEnvironmentBlock` /
    `CreateProcessAsUser` to start the Agent in the active interactive
    session from the Updater's Session-0 service process — this is why the
    Updater must run elevated. If there is no interactive session yet (or the
    launch otherwise fails), it logs and returns `false`; the caller treats
    that as "try again later" rather than fatal.
- **Why a separate service**: neither the Service nor the Agent can safely
  stop/replace/restart its own running executable; a third, always-on,
  elevated process is required to do that from the outside. That same
  process being always-on and elevated is also what lets it perform the
  *first* install (registering the Service, laying down the Agent) using the
  exact same download/apply code path as a routine update — "install" is just
  the first update check that finds nothing on disk yet.
- **Bootstrap installer (implemented)**: something has to install
  `ScreenBux.Updater` itself as a service and seed its minimal config
  (`ServerBaseUrl` at least, plus the `Service`/`Agent` install paths) before
  it can run its own first update check — the Updater can't provision itself
  out of nothing. Two bootstrap tools now exist under `installer/` (see
  [`installer/README.md`](../installer/README.md)): a parameterized PowerShell
  script (`Install-Updater.ps1`) for quick/manual use, and a WiX v5 MSI
  project (`ScreenBux.Updater.Installer`, referenced from `ScreenBux2.sln`)
  for production packaging. Neither needs to know how to install the Service
  or Agent themselves — that's still entirely the Updater's job at runtime.
- **Uninstall cleanup (implemented)**: because the Updater — not any package
  manager — installs the Service and Agent, uninstalling only the Updater
  would otherwise orphan them (left registered/running with no supported way
  to remove them). `ScreenBux.Updater` now exposes a
  `--cleanup-managed-components` console mode (see `Program.cs`) that stops
  and unregisters the managed Service (`ServiceUpdater.RemoveManagedService`,
  using `ServiceInstaller.Delete` against the SCM) and stops/removes the
  managed Agent (`AgentUpdater.RemoveManagedAgent`). Both bootstrap tools
  invoke this: the PowerShell script via `-Uninstall -RemoveManagedComponents`,
  and the MSI via a deferred custom action gated to a true removal (not a
  version upgrade).
- **Not yet implemented**: the real (non-`Static`) manifest store/CI pipeline
  that publishes update packages, package integrity verification (the
  `Sha256` field is defined but never checked), and code signing for the
  MSI/executables.

## Suggested next steps for contributors / agents

When asked to add a capability, check this doc first for whether the
underlying plumbing already exists (accounts, devices, JWTs, EF Core are all
real now) before assuming you need to build auth/identity from scratch. The
biggest genuinely-missing piece is **usage tracking/enforcement** — everything
else (multi-tenant accounts, device linking, per-account policy) has a working
first version.
