# ScreenBux2 — Status & Roadmap

_Last updated from a full source scan. This document describes the **actual** state of the code,
not the aspirational README._

---

## Part 1 — What works at the current state

### ✅ Solid / functional today

| Capability | Where | Notes |
|---|---|---|
| Regex app-blocking policy model | `PolicyRule` + `PolicyService.GetMatchingRule` | Match on `ProcessNameRegex` and/or `WindowTitleRegex`, `IgnoreCase` + `CultureInvariant`. This is the intended primary feature and it works. |
| Service enforcement loop | `ProcessMonitoringService` | Every `CheckIntervalSeconds`: enumerates all processes + the foreground window, closes matches. Self-contained; does not need the Agent. |
| Graceful close → force kill | `ProcessKillerService.TryCloseProcessAsync` | Tries `CloseMainWindow()`, waits 5s, then `Kill()`. |
| Foreground window detection | `ForegroundWindowDetector` (Service **and** Agent) | `user32.dll` P/Invoke; returns process name + window title. |
| Agent → Service reporting | `NamedPipeClient` ↔ `NamedPipeServerService` | Pipe `ScreenBuxServicePipe`, JSON messages, request/response works. |
| Policy hot-reload from disk | `PolicyService.ReloadPolicyIfChangedAsync` | Watches `LastWriteTimeUtc` on `policy.json`. |
| Policy REST API | `PolicyController` (`GET`/`PUT`/`POST reload`) | Read/write `policy.json`; `PUT` also broadcasts `PolicyUpdated`. |
| Web → Service policy sync | `PolicyController.PUT` → hub `PolicyUpdated` → `PolicySyncService` → `PolicyService.UpdatePolicyAsync` | Full loop is wired and functional when ports line up. |
| Blazor policy editor | `Components/Pages/Policy.razor` | Raw-JSON textarea editor: Load / Save. Functional but unstructured. |
| Blazor monitoring page shell | `Components/Pages/Monitoring.razor` | Connects to hub, has UI for live process list + close buttons. |

### ⚠️ Partially working / wired but incomplete

| Capability | Gap |
|---|---|
| Live process feed to the web UI | `MonitoringHub.BroadcastProcessDetection` exists but **nothing in the Service calls it** — the Service has no `IHubContext` and never invokes the hub. The Monitoring page's process table therefore stays empty in practice. |
| "Close process" from web UI | `MonitoringHub.CloseProcess` only echoes an ack back to the caller; it does **not** reach the Service (no pipe/hub bridge from server to service). |
| Kill-tree | `ProcessKillerService.GetChildProcesses` is a stub returning an empty list — only the parent process is closed. |
| Legacy time-window policy (`AppPolicy`) | Code exists (`IsWithinAllowedTime`) but is **bypassed** whenever any `PolicyRule` is present (see precedence below). `policy.json` on disk still uses the legacy `Policies` shape. |

### ❌ Declared but NOT implemented

- **Total daily time budget / usage limits.** `MaxUsageMinutesPerDay` is a property only; there is **no usage accumulation, no timer ledger, no enforcement** anywhere.
- **Accounts / parents / authentication.** No identity, no login, no authorization on API, hub, or pipe.
- **Device identity & linking.** No `DeviceId`, no registration, no per-device policy. Everything is one machine + one `policy.json`.
- **Cross-device aggregation.** Impossible today — there is no server-side persistence (no DB/EF Core) and no device concept to aggregate across.
- **Time-of-day / hours limits as a first-class rule.** Only exists in the dead legacy `AppPolicy` path, not in the primary `PolicyRule` model.

### 🐞 Known gotchas / config mismatches (verify intent before "fixing")

1. **Hub URL mismatch.** `PolicySyncService` hard-coded default is `https://localhost:7225/monitoringHub`, but all `appsettings*.json` use `:44323`. Config wins at runtime; the code default is misleading.
2. **CORS origin mismatch.** `WebServer/Program.cs` allows `http(s)://localhost:5173`, which is not the Blazor client's port.
3. **Precedence trap.** `PolicyService.ShouldBlockProcess` returns early on `Rules` — the `AppPolicy` branch is unreachable if any rule exists.
4. **Duplicated P/Invoke.** `ForegroundWindowDetector` is copy-pasted in Service and Agent.
5. **Template leftovers.** `Counter.razor` / `Weather.razor` remain in the WebClient nav.
6. **No tests, no CI.** No test project; `.github/workflows` files are empty.

---

## Part 2 — Logical next steps (roadmap)

The three things you called out — **create account**, **link devices**, and **total time across
devices** — all depend on the same missing foundation: **server-side persistence + identity +
device identity**. Below is a dependency-ordered plan. Phases 1–3 unlock everything else.

### Phase 0 — Stabilize (small, do first)
- Fix the hub URL default and CORS origin so the existing loop is reliable.
- Remove `Counter`/`Weather` template pages.
- Push detections to the web UI: give the Service an `IHubContext`-equivalent (it's a hub **client**, so have it `InvokeAsync("BroadcastProcessDetection", ...)`) or add a server endpoint the Service can post detections to. This makes the Monitoring page actually live.
- Decide the fate of the legacy `AppPolicy` model: either fold time-windows into `PolicyRule` or explicitly document it as removed.

### Phase 1 — Server-side persistence (foundation)
Introduce **EF Core** in the WebServer (SQLite for dev, SQL Server/Postgres for prod). This is the
prerequisite for accounts, devices, and cross-device aggregation. Replace the single-file
`policy.json` source-of-truth on the server with the database; keep a per-device JSON cache locally
for the Service to consume offline.

Proposed initial entities (put shared DTOs in `ScreenBux.Shared`, EF entities in a new
`ScreenBux.WebServer/Data`):
- `Account` (the parent) — `Id`, `Email`, `PasswordHash`, `CreatedAt`.
- `ChildProfile` — `Id`, `AccountId`, `DisplayName`. (A "person" whose time budget spans devices.)
- `Device` — `Id`, `AccountId`, `ChildProfileId?`, `Name`, `MachineKey`, `LinkedAt`, `LastSeenAt`.
- `PolicyDocument` — owns `Rules`/settings; scoped to `AccountId` and optionally `ChildProfileId`/`Device`.
- `UsageEvent` / `UsageLedger` — see Phase 4.

### Phase 2 — Accounts & authentication
- Add ASP.NET Core Identity (or a minimal JWT issuer) to the WebServer.
- New `AccountController`: register, login, issue token.
- Protect `PolicyController`, `MonitoringHub`, and new controllers with `[Authorize]`.
- Blazor: add a login page + auth state; send the bearer token on REST and SignalR (`AccessTokenProvider`).
- **Result: "create account" delivered.**

### Phase 3 — Device identity & linking
- On first run, the **Service** generates a stable `DeviceId` (GUID) + `MachineKey`, persisted next to `policy.json`.
- Linking flow: parent generates a short **link code** in the WebClient → enters it on the device (or the Service posts its `MachineKey` and the parent approves it in the UI). Bind `Device.AccountId` (+ `ChildProfileId`).
- Stamp `DeviceId` onto `ProcessReportMessage`, `ProcessInfo`, and all hub payloads.
- Use **SignalR groups** keyed by `AccountId` (and/or `ChildProfileId`) so a parent only sees their own devices, and so the server can target a specific device for commands.
- Per-device policy fetch: Service authenticates with its device token and pulls **its** policy from the server (`GET /api/devices/{id}/policy`) instead of assuming a co-located file.
- **Result: "link devices" delivered.**

### Phase 4 — Usage ledger + total-time enforcement (per device, then cross-device)
This is where **total time** becomes real.

1. **Measure.** The Service (which already loops on an interval and knows the foreground app) emits `UsageEvent`s: `{ DeviceId, ChildProfileId, ProcessName/RuleId, IntervalSeconds, TimestampUtc }`. Accumulate locally and flush to the server via SignalR/REST.
2. **Aggregate.** Server maintains a per-`ChildProfile` **daily usage total** (sum across all that child's devices) in the DB. A `UsageLedgerService` rolls events into per-day counters (respect a configurable timezone + reset at local midnight).
3. **Budget model.** Extend policy with a first-class budget, e.g. `TimeBudget { ChildProfileId, DailyMinutes, AppliesTo (all apps | rule group), AllowedHours (time-of-day windows) }`. This replaces the dead `MaxUsageMinutesPerDay`.
4. **Enforce.**
   - *Time-of-day (hours) limits:* each device can enforce locally from policy (no aggregation needed).
   - *Total-time-across-devices:* the server is the authority. When a child's aggregated daily total crosses the budget, the server pushes a `BudgetExceeded`/`LockNow` message to **all** of that child's connected devices (via the profile's SignalR group); the Service then blocks/closes covered apps (or locks the session).
   - Handle **offline devices**: cache the last-known remaining budget on each device so it can enforce a conservative local limit when disconnected, then reconcile on reconnect.
- **Result: "total time over multiple devices" delivered.**

### Phase 5 — Hardening
- Server → device command channel for remote "close process" / "lock now" (finish what `MonitoringHub.CloseProcess` only stubs).
- Real kill-tree (`GetChildProcesses` via WMI `Win32_Process.ParentProcessId` or Job Objects).
- Tamper-resistance: run the Service as a protected Windows Service; detect/relaunch a killed Agent.
- Consolidate the duplicated `ForegroundWindowDetector` into `ScreenBux.Shared`.
- Add a test project + wire up the empty `.github/workflows` for CI (`dotnet build` + tests).
- Audit logging of enforcement actions.

---

## Suggested implementation order (shortest path to your three goals)

1. **Phase 1** (EF Core persistence) — unblocks everything.
2. **Phase 2** (accounts/auth) — delivers *create account*.
3. **Phase 3** (device identity + linking + SignalR groups) — delivers *link devices*.
4. **Phase 4** (usage ledger + server-authoritative budget) — delivers *total time across devices*.

Phases 0 and 5 can be interleaved as capacity allows.
