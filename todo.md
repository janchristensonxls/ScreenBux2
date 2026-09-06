# TODO / Design Considerations

Running list of known design gaps and improvement ideas. Not all of these are
bugs — some are deliberate simplifications made during early development that
should be revisited before this is used by real families.

## Auto-update (Service + Agent) — status and follow-ups

Implemented: a new `ScreenBux.Updater` Windows Service (always elevated) polls
`GET api/updates/latest` on the WebServer (`UpdatesController`, anonymous,
backed by `IUpdateManifestStore`/`StaticUpdateManifestStore`, a config-driven
placeholder reading the `Updates` section of `appsettings.json`) and, when a
newer version is published, downloads the update zip and applies it.
`ServiceUpdater` now handles **both** first-time install and update: if
"ScreenBux Parental Control Service" isn't registered yet, it extracts the
package and registers + starts the service itself (`ServiceInstaller`,
P/Invoke `OpenSCManager`/`CreateService`, since `ServiceController` can't
create services); if already registered, it stops/replaces/restarts it as
before. `AgentUpdater` closes any running Agent (graceful `CloseMainWindow`
with a `Kill` fallback — a no-op if there is none yet), replaces its files,
and relaunches it in the active interactive session via `SessionLauncher`
(P/Invoke `WTSGetActiveConsoleSessionId`/`WTSQueryUserToken`/`DuplicateTokenEx`/
`CreateProcessAsUser`) — the same code path serves both a fresh install and a
routine update. See `docs/flows.md` section 5 and `docs/ARCHITECTURE.md`'s
"Auto-update" section for the full flow.

Known follow-ups / not done in v1:
- `StaticUpdateManifestStore` is a placeholder — there's no real release
  feed/CI pipeline that publishes update packages or a database-backed
  manifest yet.
- Bootstrap installer implemented: `installer/Install-Updater.ps1` (quick
  PowerShell script) and `installer/ScreenBux.Updater.Installer` (a WiX v5
  MSI project, referenced from `ScreenBux2.sln`) both install
  `ScreenBux.Updater` itself as a service and seed its config
  (`ServerBaseUrl`, `Service`/`Agent` install directories and executable
  paths). Both also support removing the Updater plus the Service/Agent it
  provisioned, via a new `ScreenBux.Updater --cleanup-managed-components`
  mode (`ServiceUpdater.RemoveManagedService` / `AgentUpdater.RemoveManagedAgent`),
  so uninstalling doesn't orphan them. See `installer/README.md`. Still
  outstanding: the MSI patches config via `util:XmlFile` which only
  understands XML even though the Updater reads JSON — needs a real JSON
  generation step (custom action) before this is CI/production-ready; no
  code signing yet either.
- No package integrity verification (the `Sha256` field on `ComponentUpdateInfo`
  is defined but never checked before applying an update).
- No rollback if a component fails to start after an update/install is applied.
- `ScreenBux.Updater` itself has no auto-update mechanism (it would need to be
  updated out-of-band, e.g. via the same installer that provisions it).

## Device time grants ("bonus time") — status and follow-ups

Implemented: a `DeviceGrant` entity/table (unique per `DeviceId`) holds a
single `ExpiresAtUtc` timestamp; while in the future, ALL enforcement on that
device is paused (both rule systems and Always/power-action rules), regardless
of the currently active policy profile/mode. WebServer (`IGrantStore`/
`EfGrantStore`) is authoritative; `DevicesController` exposes
`GET/PUT api/devices/{id}/grant` and `POST api/devices/{id}/grant/add`, and
broadcasts `GrantUpdated` over SignalR the same way policy changes broadcast
`PolicyUpdated`. The Service caches the expiry locally (`GrantService`,
`grant.json`) via the same two sync paths as policy (REST poll in
`DevicePolicySyncService`, SignalR push in `PolicySyncService`), so an active
grant keeps being honored even while disconnected from the server.
`ProcessMonitoringService` and `NamedPipeServerService` both check
`GrantService.IsGrantActive` before evaluating any rule. The Agent renders a
live countdown via a new `GrantStatusRequest`/`GrantStatusResponse` pipe
message pair; the WebClient's `LinkDevice.razor` shows a live per-device
countdown (polled every 5s) with quick add/clear controls, backed by
`GrantApiService`.

Known follow-ups / not done in v1:
- No cap on grant duration or on how far `AddMinutesAsync` can push the expiry.
- No audit trail of who granted/cleared time or when.
- Grants are not scoped per `ChildProfileId`, only per `Device` — a child with
  multiple devices needs bonus time granted separately on each.
- The WebClient countdown polls REST every 5s per device rather than relying
  solely on the `GrantUpdated` SignalR push, since that only fires on writes,
  not on the passage of time; a dedicated lightweight timer endpoint could
  reduce this if it becomes a load concern.

## Policy profiles/modes (Normal/School/Open/Sleep) — status and follow-ups

Implemented: a `PolicyProfile` entity/table (per account, optionally per
`ChildProfileId`) holds named, independently-editable policy variants, and is
the **single source of truth** for policy JSON. `PolicyDocument` is a pure
pointer (`ActivePolicyProfileId`) with no policy content of its own — it just
records which profile is currently active for the Service/Agent runtime path;
profiles are otherwise invisible to that path by design. `EfPolicyStore`/
`IPolicyStore` got `GetProfilesAsync` (seeds four built-ins: Normal, Open,
School, Sleep, on first access), `CreateProfileAsync`, `UpdateProfileAsync`,
`DeleteProfileAsync` (refuses to delete the active profile), and
`SetActiveProfileAsync` (repoints the active profile + caller broadcasts
`PolicyUpdated`). The raw-JSON "Save policy" path (`PUT /api/policy`) edits
the currently active profile directly via `GetOrCreateActiveProfileAsync`, so
it can never desync from the profile system — this replaced an earlier design
where `PolicyDocument` cached its own copy of the JSON, which could drift out
of sync with the active profile (fixed via the `DropPolicyDocumentJson`
migration, including a data migration that materializes a "Normal" profile
from any pre-existing cached JSON). New `PolicyController` endpoints:
`GET/POST /api/policy/profiles`, `PUT/DELETE /api/policy/profiles/{id}`,
`POST /api/policy/profiles/{id}/activate`. `Policy.razor` got a simple mode
button-group above the existing raw-JSON editor.

`PolicyRule` gained `ConditionKind` (`ProcessMatch` default / `Always`) and
`Action` (`CloseProcess` default / `KillProcessTree` / `Sleep` / `Hibernate`).
The built-in "Sleep" profile is a single `Always`+`Sleep` rule — the intended
replacement for a blanket "Blocked" concept, since forcing every process closed
is unsafe/incomplete compared to actually locking the machine.
`ProcessMonitoringService.EnforceAlwaysRules` evaluates `Always` rules once per
tick (short-circuiting normal per-process enforcement that tick when a power
action fires) via the new `PowerActionService` (`SetSuspendState` P/Invoke;
`Hibernate()` falls back to `Sleep()` on failure/unsupported hardware).

**Safety gate**: `PolicyService.HasSyncedSinceStartup` must be true (set by
either `DevicePolicySyncService`'s REST poll or `PolicySyncService`'s SignalR
push succeeding at least once) before `Always`/power-action rules are allowed
to fire. This exists specifically so a stale cached `policy.json` left over
from before a reboot can't put the machine back to sleep/hibernate based on
last night's mode before the Service has confirmed the real current mode with
the server. Regular `CloseProcess`/`KillProcessTree` rules are **not** gated
this way — they still run against the (possibly stale) cached policy
immediately, same as before this feature, since there's no reasonable
"offline" fallback for that and closing an app is much lower-stakes than
suspending the whole machine.

Deliberately **out of scope for this pass**: scheduling (time-of-day/day-of-
week automatic mode switching) — modes are switched manually only, from
`Policy.razor`. Also out of scope: per-child/per-device profile scoping in the
UI (the schema supports `ChildProfileId` on `PolicyProfile`, but the
controller/UI only operate on the account-wide profile list today, consistent
with the existing `PolicyDocument` scoping gap noted elsewhere in this file).

Follow-ups worth doing next:
- A friendlier profile editor in `Policy.razor` (today it's still raw JSON,
  same as before — only the mode-switch buttons are new).
- Tests for `EnforceAlwaysRules`/`HasSyncedSinceStartup` gating and
  `PowerActionService` fallback behavior in `ScreenBux.Service.Tests`.
- Consider whether `DeleteProfileAsync`'s "refuse to delete the active
  profile" behavior should surface a clearer error/confirmation in the UI
  instead of a generic 400.

## 0. Enforcement dry-run mode — status and follow-ups

**Implemented:** `ProcessKillerService` now reads `Enforcement:DryRun` from
configuration (`appsettings.json` = `false`, `appsettings.Development.json` =
`true`). In dry-run mode, `TryCloseProcessAsync`/`KillProcessTreeAsync` never
touch the target process; instead they raise
`ProcessKillerService.ProcessEnforcementAttempted` (a plain C# event) with the
process, the matching rule's name (`Reason`), and `DryRun = true`. This lets
policy/rule authoring be tested "softly" — you can watch what *would* be
closed via logs without your own dev-machine apps getting killed.
`PolicyViolationLoggerService` is a minimal `IHostedService` subscriber that
just logs every attempt; it's a template for future subscribers (e.g. a
SignalR broadcast to the WebClient for live dry-run visibility, or a
"detections" feed on the Monitoring page).

**Covered by tests:** `tests/ScreenBux.Service.Tests` — `ProcessKillerService`
dry-run vs. live behavior (process left alone vs. actually killed, in both
cases the event fires with the correct `Reason`/`Action`/`DryRun`), and
`PolicyViolationLoggerService`'s subscribe/unsubscribe lifecycle.

**Not yet done / consider next:**
- No test exercises `ProcessMonitoringService.EnforcePoliciesAsync` end-to-end
  (i.e. that a matching `PolicyRule` actually results in a call to
  `ProcessKillerService` with the right reason) — current tests only cover
  `ProcessKillerService` in isolation. Worth adding once `PolicyService`/rule
  matching has a seam for injecting a fake process list (today it always
  calls `Process.GetProcesses()` directly).
- Dry-run is a `ProcessKillerService`-level switch, so it also silently
  applies to any *other* future caller of `TryCloseProcessAsync`/
  `KillProcessTreeAsync` — worth keeping in mind if a new enforcement path is
  added.
- Consider surfacing the current dry-run state somewhere visible at runtime
  (e.g. a log line at startup, or exposed via the Named Pipe `GetPolicy`
  response) so it's obvious when the Service is running in dry-run vs. live
  mode without checking `appsettings.json`.

## 0.1 Service-side foreground-window detection relic — RESOLVED

**Was:** `ProcessMonitoringService` held its own `ForegroundWindowDetector`
instance and called `GetMatchingRule(foregroundProcess, true)` against
whatever `GetForegroundWindow()` returned inside the Service process itself.
This never worked correctly once the Service runs as a real installed Windows
Service, because Session 0 services run on a non-interactive window station
and cannot see the interactive user's desktop — `foregroundProcess` was
effectively always `null` in that scenario (confirmed via debugging: the field
returned the debugger's own view, not the user's).

**Fixed:**
- Removed the Service-side `ForegroundWindowDetector` field/usage and the
  leftover debug code (`dbgProcesses`, the `"blox"` breakpoint hook) from
  `ProcessMonitoringService`. Its bulk `Process.GetProcesses()` loop now always
  passes `isForegroundWindow: false`, so it only ever matches `ProcessNameRegex`
  / legacy name-based policy — an honest reflection of what it can actually see.
- Deleted `src/ScreenBux.Service/Services/ForegroundWindowDetector.cs` (it had
  zero remaining references).
- `NamedPipeServerService.HandleProcessReportAsync` — which receives the
  Agent's real, interactive-session foreground process/window report — is now
  the sole place `WindowTitleRegex` rules are evaluated
  (`GetMatchingRule(message.Process, isForegroundWindow: true)`), and it now
  respects `Enforcement:DryRun` and reports the actual matching rule's name as
  the reason, instead of always instructing the Agent to close with a generic
  message.

**Not yet done / consider next:**
- No automated test yet covers `NamedPipeServerService.HandleProcessReportAsync`
  directly (dry-run behavior, rule-name propagation). Existing tests only cover
  `ProcessKillerService`/`PolicyViolationLoggerService` in isolation.
- The Agent-reported `ProcessInfo.WindowTitle` currently only reflects a single
  foreground window per poll interval; if title-rule matching needs to see
  background/non-foreground windows too, additional Agent-side reporting would
  be needed.

## 0.2 Enforcement moved to the Service; kill-tree stub replaced — RESOLVED

**Was (two related weaknesses carried over from the predecessor app):**
1. Enforcement of Agent-reported (foreground/title) violations was done by
   telling the *Agent* to close the process, in the Agent's own normal
   user-session token — no benefit from the Service's elevated rights (e.g.
   LocalSystem when installed as a real Windows Service), so admin-launched or
   otherwise protected processes could resist termination exactly like in the
   old app.
2. `ProcessKillerService.KillProcessTreeAsync`'s child-process enumeration
   (`GetChildProcesses`) was a stub that always returned an empty list — it
   never actually killed a process tree, just the single reported PID. For
   multi-process apps (e.g. Chromium-based browsers like Avast Browser, which
   spawn renderer/GPU child processes per tab), killing only the main/window
   process could leave orphaned children running, or fail to take down the
   app if the reported PID wasn't the true top-level owner.

**Fixed:**
- `NamedPipeServerService.HandleProcessReportAsync` now enforces directly via
  `ProcessKillerService.TryCloseProcessAsync` on the Service side instead of
  returning a `CloseProcessCommand` as the primary path. Windows processes are
  machine-global, so the Service can open a handle to the reported PID
  directly without needing the Agent as a proxy. Only if the Service's own
  attempt fails (e.g. access denied on a protected process) does it fall back
  to asking the Agent to try a graceful, in-session `CloseMainWindow()` — a
  legitimate UI action that doesn't require elevation, unlike forceful kill.
- Removed the now-obsolete `ProcessKillerService.NotifyRemoteEnforcementAttempt`
  helper (it existed only to support the old Agent-does-the-killing path).
- Replaced the stubbed `GetChildProcesses`/manual child-kill loop with the
  built-in `Process.Kill(entireProcessTree: true)` overload (.NET 5+), which
  correctly walks the real process snapshot to find and kill descendants. Used
  both by `ProcessKillerService.KillProcessTreeAsync` (Service-side) and the
  Agent's `MonitoringService.TryCloseProcessAsync` fallback path.

**Not yet done / consider next:**
- `Process.Kill(true)` is best-effort per-process — if a child is itself
  protected/elevated beyond the caller's rights, that one kill fails silently
  while siblings still get attempted. Worth adding a `taskkill /PID {pid} /T /F`
  fallback as a last resort if this turns out to be needed in practice.
- No automated test yet covers `NamedPipeServerService.HandleProcessReportAsync`'s
  Service-side enforcement + Agent fallback branching.

## 1. Device redemption silently re-parents an already-linked device

**Where:** `DevicesController.Redeem` (`src/ScreenBux.WebServer/Controllers/DevicesController.cs`)

**Current behavior:** Redeeming a `DeviceLinkCode` is the only gate on binding a
`Device` to an `Account`. If a `Device` with the same `MachineKey` already
exists and is linked to a *different* account, `Redeem` overwrites
`device.AccountId` / `device.ChildProfileId` with the new link code's values,
with no check, warning, or confirmation step:

```csharp
device.AccountId = linkCode.AccountId;
device.ChildProfileId = linkCode.ChildProfileId;
```

**Why this is fine for now:** During early development this is convenient —
re-running the Service against a fresh test account, or re-linking a dev
machine, "just works" without manually clearing state in the database.

**Why this is a problem for a real product:** A stray, leaked, or
maliciously-guessed link code (codes are only 8 characters, unambiguous
alphabet, 15-minute expiry — brute-forceable in principle) could silently
hijack an already-provisioned device away from its rightful parent account,
with no audit trail beyond the `DeviceLinkCode` row itself. There's also no
signal to the original parent that "their" device just switched ownership.

**Proposed fix (before shipping):**
- In `Redeem`, if the matched `Device` already has a non-null `AccountId`
  that differs from `linkCode.AccountId`, reject the redemption (or require
  an explicit "transfer device" flow initiated by the *current* owning
  parent, not just possession of a new code).
- Consider notifying/logging when a device's `AccountId` changes, and
  surfacing that in the WebClient device list.
- Consider rate-limiting/shortening code lifetime further and/or requiring
  the requesting device to already be "known" in some way.

## 2. Multi-parent access to a child's device(s)

**Motivating scenario:** Two parents (e.g. co-parenting across two
households) both want to manage policy / view activity for the same child,
who may use one or more devices.

**Why this should *not* be modeled as one `Device` belonging to multiple
`Account`s:** `Device.AccountId` currently represents both "who owns/enforces
policy for this device" *and* implicitly "who is allowed to manage it" as a
single 1:1 relationship. Enforcement (`ProcessMonitoringService`, policy
sync) is inherently single-owner: the Service holds one device token scoped
to one account. Making `Device` a many-to-many with `Account` would conflate
"who runs enforcement for this box" with "who is allowed to view/edit its
policy," and would require rethinking `DevicePolicySyncService`/device
tokens/`EfPolicyStore` scoping for ambiguous ownership.

**Proposed direction:** Model device *ownership* (1:1, as today) separately
from device/child *access* (many-to-many). E.g. a new entity such as:

```csharp
public class AccountShare
{
	public Guid Id { get; set; }
	public string OwnerAccountId { get; set; }      // the account that owns the ChildProfile/Device
	public string SharedWithAccountId { get; set; } // the co-parent's account
	public Guid? ChildProfileId { get; set; }        // scope: a whole child...
	public Guid? DeviceId { get; set; }               // ...or a specific device
	public AccountShareRole Role { get; set; }        // e.g. Viewer, CoParent
}
```

Controllers that check `Device.AccountId == User.GetAccountId()` for
authorization (`DevicesController.ListDevices`/`GetDevicePolicy`,
`PolicyController`) would need to also allow access when a matching
`AccountShare` grants it. This keeps enforcement plumbing untouched and adds
sharing purely as an authorization concern.

**Not yet started** — no entity, migration, or controller changes exist for
this today.
