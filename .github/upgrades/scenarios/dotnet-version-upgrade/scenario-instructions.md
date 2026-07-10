# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0 (.NET 10, LTS)

## Source Control
- **Source Branch**: V20260709
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: Single Commit at End
- **Branch Sync**: Auto (Merge)

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: All-at-Once

## Strategy
**Selected**: All-at-Once
**Rationale**: 5 projects all on .NET 8, shallow 2-tier dependency graph, no incompatible packages, no high-risk migrations, no CI-green constraint.

### Execution Constraints
- Single atomic upgrade — retarget all 5 project TFMs and bump packages together, do not upgrade project-by-project in dependency order.
- Sequence within the upgrade task: update all `.csproj` TFMs → update all package references → restore → build full solution and fix all errors/warnings in one bounded pass → verify 0 errors/0 warnings.
- Run tests only AFTER the atomic upgrade builds cleanly.
- Full-solution clean build (0 errors, 0 warnings) is the completion gate for the upgrade.
- Commit strategy is Single Commit at End — do not commit intermediate states (solution may be temporarily broken mid-upgrade).

## Key Decisions Log
- **2026 post-upgrade config fix**: Converged all hub/API endpoints on port `44323`. The originally-noted issues (PolicySyncService default `:7225`, CORS origin `:5173`) were already fixed on the source branch. The real remaining mismatch was the WebServer Kestrel `https` profile binding `:7225` while all clients target `:44323`, plus WebClient/Program.cs falling back to `:7225`. Fixed WebServer `launchSettings.json` https profile → `44323` and WebClient `Program.cs` PolicyApiBaseUrl fallback → `44323`. CORS list left unchanged (already correct). Build verified 0/0.
- **2026 post-upgrade runtime exceptions (pipe + process)**: Two pre-existing latent bugs surfaced when running under the debugger (not caused by the .NET 10 upgrade). (1) `NamedPipeServerService.HandleClientAsync` threw `ObjectDisposedException: Cannot access a closed pipe` because the pipe was `await using`-scoped in the accept loop but handled in a fire-and-forget `Task.Run` — the loop disposed it mid-read. Fix: transferred pipe ownership to the handler (disposes via `await using` there), added a zero-byte-read (client disconnect) guard, and split catches so IOException/ObjectDisposedException/OperationCanceledException log at Debug. (2) `Win32Exception` flood from `ProcessMonitoringService` calling `process.MainModule?.FileName` on every process each cycle (access-denied on protected/system processes). Fix: `ExecutablePath` is now resolved only when the legacy AppPolicy path can matter (`!Rules.Any(enabled) && Policies.Count > 0`), so `MainModule` is not touched under the default regex-Rules config. Build verified 0/0.
- **2026 Swagger "invalid version field" — CLIENT CACHE, not a code bug**: Symptom was `https://localhost:44323/swagger/index.html` showing "does not specify a valid version field". Extensive server-side probing PROVED the app was correct: served `swagger.json` is valid JSON (no BOM, `openapi: 3.0.4`), `index.js` referenced the correct definition URL, and the freshly-served `swagger-ui-bundle.js` (v18.3.1) validation regex `/^3\.0\.(?:[1-9]\d*|0)$/` accepts `3.0.4`. Root cause: the browser was running a STALE pre-upgrade `swagger-ui-bundle.js` whose older validator only accepted `3.0.0`–`3.0.3`; the .NET 10 upgrade bumped Microsoft.OpenApi to emit `openapi: 3.0.4`. Resolution: hard refresh / empty-cache reload (confirmed by user). Earlier "relative URL" hypothesis was DISPROVEN. Kept the absolute `SwaggerEndpoint("/swagger/v1/swagger.json", ...)` as harmless robustness only. Optional not-yet-applied hardening: add no-cache headers on Swagger UI static assets to prevent a stale bundle masking future upgrades.
