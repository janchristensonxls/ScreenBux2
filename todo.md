# TODO / Design Considerations

Running list of known design gaps and improvement ideas. Not all of these are
bugs — some are deliberate simplifications made during early development that
should be revisited before this is used by real families.

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
- `ProcessKillerService` gained a public `IsDryRun` property and a
  `NotifyRemoteEnforcementAttempt(...)` method so the pipe-driven remote-close
  path (where the Agent, not the Service, performs the actual close) still
  raises the same `ProcessEnforcementAttempted` event/dry-run semantics as the
  local kill paths, keeping `PolicyViolationLoggerService` and future
  subscribers consistent regardless of which side executes the close.

**Not yet done / consider next:**
- No automated test yet covers `NamedPipeServerService.HandleProcessReportAsync`
  directly (dry-run behavior, rule-name propagation). Existing tests only cover
  `ProcessKillerService`/`PolicyViolationLoggerService` in isolation.
- The Agent-reported `ProcessInfo.WindowTitle` currently only reflects a single
  foreground window per poll interval; if title-rule matching needs to see
  background/non-foreground windows too, additional Agent-side reporting would
  be needed.

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
