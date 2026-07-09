# .NET 10 Version Upgrade Plan

## Overview

**Target**: Upgrade all 5 ScreenBux2 projects from .NET 8 to .NET 10 (LTS).
**Scope**: Small solution — 5 SDK-style projects, 4 package upgrades, no incompatible packages, no security vulnerabilities.

### Selected Strategy
**All-At-Once** — All projects are upgraded simultaneously in a single atomic operation.
**Rationale**: 5 projects, all already on .NET 8, shallow 2-tier dependency graph (Shared → Agent/WebClient/WebServer/Service), no high-risk package migrations, and no CI-green constraint. Incremental strategies would add multi-targeting overhead with no benefit at this scale.

**Projects** (single group — no tiers):
- `src/ScreenBux.Shared` — class library → `net10.0`
- `src/ScreenBux.Agent` — WPF → `net10.0-windows`
- `src/ScreenBux.WebClient` — Blazor Server → `net10.0`
- `src/ScreenBux.WebServer` — ASP.NET Core Web API + SignalR → `net10.0`
- `src/ScreenBux.Service` — Worker Service → `net10.0`

## Tasks

### 01-prerequisites: Verify toolchain and pin SDK

Confirm the local environment can build .NET 10 before touching any project. Verify the .NET 10 SDK is installed and available to the IDE and CLI. Check whether a `global.json` exists at the repo root or anywhere in the solution tree — if one pins an older SDK (e.g., an 8.0.x band), it must be updated to allow .NET 10 or it will silently block the build; if none exists, no action is needed (roll-forward will pick the installed SDK).

This is a low-risk verification task. The main risk is an environment mismatch surfacing later as confusing build errors, so catching it up front saves debugging time.

**Done when**: The .NET 10 SDK is confirmed installed, and any `global.json` SDK pin is compatible with `net10.0` (or confirmed absent).

### 02-upgrade-all-projects: Retarget all projects to .NET 10 and update packages

Retarget all 5 projects in a single atomic pass: change `net8.0` → `net10.0` (and `net8.0-windows` → `net10.0-windows` for the WPF Agent) in each `.csproj`, then bump the recommended packages to their `10.0.9` equivalents — `Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.Extensions.Hosting`, and `Microsoft.Extensions.Hosting.WindowsServices`. `Swashbuckle.AspNetCore` (6.6.2) is already compatible and needs no change unless a build issue requires it. Restore, then build the full solution and fix all compilation errors in one bounded pass.

The assessment flagged 144 issues, but the overwhelming majority (112 `Api.0001` binary-incompatibility flags, mostly in the WPF Agent) are resolved simply by recompiling against .NET 10. The remaining items to watch are 10 `Api.0002` source-incompatible and 12 `Api.0003` behavioral-change flags spread across the Service, WebClient, and WebServer — these may need small code adjustments. Research starting points: review ASP.NET Core / SignalR behavioral changes between 8 and 10 for the WebServer hub and WebClient/Service SignalR clients, and confirm the Worker Service hosting APIs (`Microsoft.Extensions.Hosting.WindowsServices`) still bind the same way. Do not fix the known pre-existing config quirks (hub URL/port mismatch, CORS origin) as part of this task unless the upgrade itself breaks them — they are out of scope.

**Done when**: All 5 `.csproj` files target `net10.0`/`net10.0-windows`, the 4 packages are updated to `10.0.9`, the full solution builds with **0 errors and 0 warnings**, and no new API/behavioral regressions are left unaddressed.

### 03-final-validation: Build solution and run tests

Perform the final end-to-end validation of the upgraded solution. Run a clean full-solution build and confirm 0 errors and 0 warnings across all 5 projects. Discover and run any test projects; note that the repository currently has no test project, so if none is found, record that there is no automated test coverage to run rather than treating it as a failure.

Capture any deferred, non-blocking recommendations surfaced during the upgrade (e.g., the pre-existing hub URL/port and CORS mismatches noted in the assessment) so they are visible but not silently bundled into this upgrade.

**Done when**: The solution builds cleanly (0 errors, 0 warnings), all discovered tests pass (or the absence of tests is documented), and any deferred recommendations are recorded.
