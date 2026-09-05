# Data & Message Flows

Companion to [`ARCHITECTURE.md`](ARCHITECTURE.md). Where that document describes the
*static* shape of the system (projects, entities, known gaps), this document traces the
*dynamic* behavior actor-by-actor: who sends what to whom, in what order, and exactly which
method to put a breakpoint in if you want to watch it happen.

Actors referenced throughout:
- **WebClient** — Blazor Server parent UI (`src/ScreenBux.WebClient`)
- **WebServer** — ASP.NET Core REST API + SignalR hub (`src/ScreenBux.WebServer`)
- **Hub** — `MonitoringHub`, hosted inside WebServer but treated separately here since it has
  its own connection/group lifecycle independent of the REST controllers
- **Service** — the Windows Service enforcement engine (`src/ScreenBux.Service`)
- **Agent** — the WPF app running in the interactive user session (`src/ScreenBux.Agent`)

---

## 1. Device linking & SignalR group membership

### Who ends up in the same "group", and why

Both the parent's browser tab (WebClient) and the child's PC (Service) end up in **the same
SignalR group**, and that group is simply **the account's ID as a string**. There is no
separate "device group" or "family group" concept — everyone who authenticates with a JWT
carrying a given `account_id` claim lands in the group named after that `account_id`.

```
MonitoringHub.OnConnectedAsync()                     src/ScreenBux.WebServer/Hubs/MonitoringHub.cs:20
	accountId = Context.User?.GetAccountId()          // works for BOTH account and device tokens
	Groups.AddToGroupAsync(Context.ConnectionId, accountId)
```

This works because `JwtTokenService` puts `account_id` on **both** token shapes:

| Token shape | Claims | Issued to | Issued by |
|---|---|---|---|
| Account token | `account_id`, `token_type=account` | Parent (WebClient) | `AccountController` login |
| Device token | `account_id`, `device_id`, `token_type=device` | Service | `DevicesController.Redeem` |

`GetAccountId()`/`GetDeviceId()` (`src/ScreenBux.WebServer/Services/ClaimsPrincipalExtensions.cs`)
are the two claim readers used everywhere on the server side to answer "whose data is this."

### Sequence: parent generates a code, device redeems it, both connect to the hub

```
Parent clicks "Generate link code" in WebClient
  -> POST api/devices/linkcode                        DevicesController.GenerateLinkCode  (JWT: account token)
	 creates DeviceLinkCode { Code, ExpiresAt = +15min }

Parent types the code into the Service (or it's supplied via config on first run)
Service -> POST api/devices/redeem                     DevicesController.Redeem  (anonymous; body: Code, MachineKey, DeviceName)
	 src/ScreenBux.Service/Services/DevicePolicySyncService.cs: RedeemCodeAsync()
	 - DeviceIdentityService.GetOrCreate() supplies the stable DeviceId/MachineKey (persisted in device.json)
	 - on success, WebServer returns { DeviceId, Token } (a device token)
	 - Service saves the token into device.json via _deviceIdentity.Save(state)

Parent clicks "Connect" in the Monitoring page
  -> MonitoringService.StartAsync()                    src/ScreenBux.WebClient/Services/MonitoringService.cs
	 HubConnection built with AccessTokenProvider = account token
	 -> hub.StartAsync() -> MonitoringHub.OnConnectedAsync() adds this connection to group "{accountId}"

Service's background loop (independently, once linked)
  -> PolicySyncService.ExecuteAsync()                   src/ScreenBux.Service/Services/PolicySyncService.cs
	 waits until DeviceIdentityService.GetOrCreate().DeviceToken is non-empty (polls every 10s)
	 HubConnection built with AccessTokenProvider = device token
	 -> hub.StartAsync() -> MonitoringHub.OnConnectedAsync() adds this connection to the SAME group "{accountId}"
```

Breakpoint suggestions to observe this: `MonitoringHub.OnConnectedAsync` (see which
`accountId` each connection resolves to — a parent tab and a Service should show the *same*
value), `DevicesController.Redeem` (confirm the `Device` row gets created/updated), and
`PolicySyncService.ExecuteAsync`'s `while` loop around the `StartAsync()` call (confirm it
actually reaches `_logger.LogInformation("SignalR connected...")`, not stuck retrying).

---

## 2. Policy: cache, sync, and when monitoring actually starts

### The two independent caches

| Store | Owner | Format | Source of truth for |
|---|---|---|---|
| SQL Server (`PolicyProfiles` / `PolicyDocuments`) | WebServer | EF Core rows | What the parent authored |
| `policy.json` (`%CommonApplicationData%\ScreenBux\policy.json`) | Service | Flat JSON file | What the enforcement loop actually reads |

The Service **never** talks to SQL Server. It only ever sees policy content that has been
pushed or pulled into its local `policy.json`. There are two independent, redundant paths
that keep that file in sync:

```
Path A — REST poll (always running, works even if SignalR is broken)
  DevicePolicySyncService.ExecuteAsync()                src/ScreenBux.Service/Services/DevicePolicySyncService.cs:42
	  loop every DevicePolicySyncIntervalSeconds (config; default 60s)
		EnsureLinkedAsync()  — redeems a link code if not yet linked
		FetchPolicyAsync()                              …DevicePolicySyncService.cs:139
		  GET api/devices/{deviceId}/policy             DevicesController.GetDevicePolicy
		  writes response straight to policy.json
		  _policyService.MarkSyncedSinceStartup()

Path B — SignalR push (near-instant, but silently does nothing if the hub connection never lands)
  PolicySyncService.ExecuteAsync()                       src/ScreenBux.Service/Services/PolicySyncService.cs:30
	  hubConnection.On<PolicyConfiguration>("PolicyUpdated", async policy => {
		  _policyService.UpdatePolicyAsync(policy)       …PolicyService.cs:110 — writes policy.json AND updates in-memory _configuration immediately
		  _policyService.MarkSyncedSinceStartup()
	  })
```

`PolicyService.UpdatePolicyAsync` (`src/ScreenBux.Service/Services/PolicyService.cs:110`) is
the one method both paths funnel through eventually (Path A writes the file directly and lets
the next poll's `ReloadPolicyIfChangedAsync` pick it up; Path B calls `UpdatePolicyAsync`
directly, which updates the in-memory config *and* the file in the same call — so Path B is
the only one that's actually low-latency).

### When does monitoring actually start enforcing?

```
ProcessMonitoringService.ExecuteAsync()                  src/ScreenBux.Service/Services/ProcessMonitoringService.cs:42
	await _policyService.LoadPolicyAsync()               — one-time load of whatever policy.json currently contains
	loop forever:
		await _policyService.ReloadPolicyIfChangedAsync()  — polls file mtime, no FileSystemWatcher
		if config.EnableMonitoring:
			EnforceAlwaysRules()  or  EnforcePoliciesAsync()
		delay config.CheckIntervalSeconds (default 5s)
```

**Important: there is no wait/timeout gate on the enforcement loop itself.** It starts
enforcing from whatever is in `policy.json` on disk *immediately* on Service startup — it does
**not** wait for either sync path to succeed first. The only thing that is gated is
`EnforceAlwaysRules()`'s power actions:

```
EnforceAlwaysRules()                                     …ProcessMonitoringService.cs:76
	if (!_policyService.HasSyncedSinceStartup) return false;   // only gates Sleep/Hibernate rules
	...
```

So: regular `CloseProcess`/`KillProcessTree` rules run against a **potentially stale,
pre-reboot cached policy** the instant the Service starts, while `Sleep`/`Hibernate` "Always"
rules are deliberately withheld until at least one sync (REST or SignalR) has completed.

### Saving a policy — who is affected, and how

```
Parent clicks "Save" in Policy.razor                     src/ScreenBux.WebClient/Components/Pages/Policy.razor: SavePolicyAsync()
	deserializes the textarea JSON -> PolicyConfiguration
	-> PolicyApiService.UpdatePolicyAsync(policy)         src/ScreenBux.WebClient/Services/PolicyApiService.cs
	   PUT api/policy  (JWT: account token)
		 -> PolicyController.UpdatePolicy                  src/ScreenBux.WebServer/Controllers/PolicyController.cs:49
			  _policyStore.SavePolicyAsync(accountId, policy)
				-> EfPolicyStore.GetOrCreateActiveProfileAsync(accountId)   ***edits whichever PolicyProfile is currently active***
				-> profile.PolicyJson = serialize(policy); SaveChangesAsync()
			  _hubContext.Clients.Group(accountId).SendAsync("PolicyUpdated", policy)
				-> every connection in group "{accountId}" receives it:
					 - WebClient's own MonitoringService (if the parent also clicked "Connect") — fires an event, no enforcement effect
					 - Service's PolicySyncService.On<PolicyConfiguration>("PolicyUpdated", ...) — writes policy.json + marks synced
```

**Receivers, concretely:** only Services (devices) linked to this account and currently
connected to the hub are affected in real time. Any Service that is offline, or whose SignalR
connection never established (see Known Issues below), will not see the change until its next
`DevicePolicySyncService` REST poll (up to 60s later by default).

### Switching mode ("activate profile") — who is affected, and how

```
Parent clicks a mode button in Policy.razor               ActivateProfileAsync(profileId)
	-> PolicyApiService.ActivateProfileAsync(profileId)
	   POST api/policy/profiles/{profileId}/activate
		 -> PolicyController.ActivateProfile               src/ScreenBux.WebServer/Controllers/PolicyController.cs:154
			  _policyStore.SetActiveProfileAsync(accountId, profileId)
				-> EfPolicyStore.SetActiveProfileAsync: repoints PolicyDocument.ActivePolicyProfileId at the chosen profile
			  _hubContext.Clients.Group(accountId).SendAsync("PolicyUpdated", policy)   // same broadcast as a raw save
```

Editing a *non-active* profile in isolation (`PUT api/policy/profiles/{id}`,
`PolicyController.UpdateProfile`) only broadcasts `PolicyUpdated` **if that profile happens to
be the active one** (`if (profile.IsActive) { ... }`) — editing an inactive profile correctly
does nothing to any running Service, since it isn't in effect anywhere yet.

**Net effect:** both "Save" and "Activate" ultimately converge on the exact same signal
(`PolicyUpdated` broadcast to the account's SignalR group, carrying a `PolicyConfiguration`
payload) and the exact same receiver (`PolicySyncService`'s hub handler on the Service). There
is no behavioral difference between them once the message leaves the WebServer — the
difference is only in *which row in SQL Server got edited* beforehand.

---

## 3. Agent foreground-window detection & reporting

```
Agent starts (MainWindow_Loaded)                          src/ScreenBux.Agent/MainWindow.xaml.cs
	_monitoringService.Start()                             src/ScreenBux.Agent/Services/MonitoringService.cs: Start()
	   starts a DispatcherTimer, interval = 2 seconds

Every tick:
	OnTimerTick()                                           …Agent/Services/MonitoringService.cs
		ForegroundWindowDetector.GetForegroundProcessInfo()  src/ScreenBux.Agent/Services/ForegroundWindowDetector.cs
			user32.dll: GetForegroundWindow / GetWindowThreadProcessId / GetWindowText(Length)
		only reports if ProcessId or WindowTitle changed since last tick (dedup via _lastReportedProcess)
		-> NamedPipeClient.SendMessageAsync(ProcessReportMessage)   src/ScreenBux.Agent/Services/NamedPipeClient.cs
			 pipe "ScreenBuxServicePipe", 5s timeout, JSON payload

Service side:
	NamedPipeServerService.HandleClientAsync -> ProcessMessageAsync   src/ScreenBux.Service/Services/NamedPipeServerService.cs:149
		case "ProcessReport" -> HandleProcessReportAsync(message)      …NamedPipeServerService.cs:210
			rule = _policyService.GetMatchingRule(message.Process, isForegroundWindow: true)   // <-- only place isForegroundWindow is ever true
			shouldBlock = rule != null || _policyService.ShouldBlockProcess(...)
			if rule.Action == KillProcessTree -> _processKiller.KillProcessTreeAsync(...)
			else -> _processKiller.TryCloseProcessAsync(...)          // graceful close attempted service-side first
			on failure (e.g. access denied) -> returns CloseProcessCommand back over the pipe
				-> Agent's MonitoringService receives it -> TryCloseProcessAsync() (CloseMainWindow, best-effort, same-session fallback)
```

Breakpoint suggestions: `ForegroundWindowDetector.GetForegroundProcessInfo` (confirm it's
firing every 2s and picking up the right window), `NamedPipeServerService.HandleProcessReportAsync`
(confirm `message.Process.WindowTitle` is populated and `GetMatchingRule` is being called with
`isForegroundWindow: true`), `PolicyService.GetMatchingRule` (step through the regex match
against `rule.WindowTitleRegex`).

---

## 4. Device time grants ("bonus time" that pauses all enforcement)

```
Parent (WebClient LinkDevice.razor)
	AddMinutesAsync / ClearGrantAsync / SetGrantAsync            src/ScreenBux.WebClient/Services/GrantApiService.cs
	-> PUT/POST api/devices/{id}/grant[/add]                     src/ScreenBux.WebServer/Controllers/DevicesController.cs
		EfGrantStore.SetGrantAsync / AddMinutesAsync           src/ScreenBux.WebServer/Services/EfGrantStore.cs
			writes DeviceGrant.ExpiresAtUtc (1 row per device, unique on DeviceId)
		-> hubContext.Clients.Group(accountId).SendAsync("GrantUpdated", grant)

Service side (kept in sync exactly like policy, two parallel paths):
	DevicePolicySyncService polls GET api/devices/{id}/grant every cycle       src/ScreenBux.Service/Services/DevicePolicySyncService.cs
	PolicySyncService listens for the SignalR "GrantUpdated" push             src/ScreenBux.Service/Services/PolicySyncService.cs
	Both call GrantService.UpdateGrantAsync(expiresAtUtc), which persists to
	local grant.json next to policy.json                                     src/ScreenBux.Service/Services/GrantService.cs

Enforcement gate (checked BEFORE any rule evaluation, so a grant pauses
everything - regular Rules, legacy AppPolicy, and Always/power-action rules alike):
	ProcessMonitoringService.ExecuteAsync: if (_grantService.IsGrantActive) skip tick's enforcement
	NamedPipeServerService.HandleProcessReportAsync: if (_grantService.IsGrantActive) return success without blocking

Agent countdown:
	MainWindow polls GrantStatusRequest/GrantStatusResponse over the named pipe
	every 5s (piggybacked on the existing service-status timer) and renders
	"Bonus time active: HH:MM:SS remaining".

WebClient countdown:
	LinkDevice.razor polls GrantApiService.GetGrantAsync per device every 5s
	via a System.Threading.Timer, independent of the SignalR GrantUpdated event
	(which only fires on a write, not a tick).
```

`IsGrantActive` is always the stateless comparison `ExpiresAtUtc > DateTime.UtcNow`
evaluated from whatever the Service's local `grant.json` currently says - the
Service keeps honoring an active grant even while completely disconnected from
the WebServer, the same way it keeps enforcing stale cached policy.

---



These were discovered while writing this document and are the most likely explanations for
"switching modes / saving a policy doesn't affect behavior as expected":

1. **`WindowTitleRegex` rules only ever match via the Agent's foreground report, never during
   the Service's own background scan.** `ProcessMonitoringService.EnforcePoliciesAsync` (the
   loop that walks `Process.GetProcesses()` every `CheckIntervalSeconds`) always calls
   `_policyService.GetMatchingRule(processInfo, isForegroundWindow: false)` — hardcoded
   `false` (`src/ScreenBux.Service/Services/ProcessMonitoringService.cs:127`). Since
   `GetMatchingRule` only checks `WindowTitleRegex` when `isForegroundWindow` is `true`
   (`src/ScreenBux.Service/Services/PolicyService.cs`, `GetMatchingRule`), **any rule written
   with only a `WindowTitleRegex` and no `ProcessNameRegex` will never fire from the
   background scan** — it only fires the moment that window happens to be in the foreground
   *and* the Agent is running, connected, and has reported it. If the Agent is closed or the
   pipe connection is down, such a rule effectively does nothing, even though the policy
   editor gives no indication of this dependency. Worth surfacing in the UI or at minimum
   documenting per-rule.

2. **`MonitoringHubUrl` and `ServerBaseUrl` are two independently-configured settings that
   must point at the same server, but nothing enforces or validates that.**
   `PolicySyncService` reads `_configuration["MonitoringHubUrl"]` (defaulting to
   `https://localhost:44323/monitoringHub` if unset), while `DevicePolicySyncService`/device
   redemption use `_configuration["ServerBaseUrl"]`. If only `ServerBaseUrl` is set in a real
   deployment (the more "obvious" setting to configure) and `MonitoringHubUrl` is left at its
   localhost default, the SignalR push path (`PolicySyncService`) will spin forever trying to
   reach `localhost:44323` and never succeed — but this fails **silently** (just retried
   `LogWarning`s), so mode switches and policy saves will appear to "not work" in real time
   and only actually apply after the next `DevicePolicySyncIntervalSeconds` REST poll (60s
   default). This is very likely what's being observed as "not working as expected" — check
   the Service's logs for repeated `"Failed to connect to SignalR, retrying..."` to confirm.

3. **Power-action ("Sleep"/"Hibernate") "Always" rules are correctly gated behind
   `HasSyncedSinceStartup`, but that gate can be starved by issue #2 above.** If SignalR never
   connects, `HasSyncedSinceStartup` only becomes `true` after the first REST poll succeeds —
   up to 60 seconds after Service startup, and this only happens once; after that it stays
   `true` for the rest of the process lifetime. So a "Sleep" mode activated right after the
   Service starts (e.g., during testing/dev iteration where the Service is frequently
   restarted) can appear to silently do nothing for up to a minute — not a bug in the gate
   itself, but worth knowing when debugging "why didn't Sleep mode trigger."

4. **Regular `CloseProcess`/`KillProcessTree` rules are *not* gated by `HasSyncedSinceStartup`
   at all**, so they act on whatever `policy.json` contains from a previous session the
   instant the Service starts — if the Service was stopped while "Open" mode was active and
   restarted after the parent had since switched to "School" mode (while the Service was
   down), the Service will enforce **stale "Open" mode rules** for up to 60 seconds (or until
   SignalR reconnects) before catching up. Combined with issue #2, this window can be
   indefinite.
