# 01-prerequisites: Verify toolchain and pin SDK

Confirm the local environment can build .NET 10 before touching any project. Verify the .NET 10 SDK is installed and available to the IDE and CLI. Check whether a `global.json` exists at the repo root or anywhere in the solution tree — if one pins an older SDK (e.g., an 8.0.x band), it must be updated to allow .NET 10 or it will silently block the build; if none exists, no action is needed (roll-forward will pick the installed SDK).

This is a low-risk verification task. The main risk is an environment mismatch surfacing later as confusing build errors, so catching it up front saves debugging time.

**Done when**: The .NET 10 SDK is confirmed installed, and any `global.json` SDK pin is compatible with `net10.0` (or confirmed absent).

## Research Findings

- **.NET 10 SDK**: Installed — `dotnet --version` reports `10.0.301` (the active/default SDK). `validate_dotnet_sdk_installation` for `net10.0` returned "Compatible SDK found".
- **Installed SDKs**: 3.1.426, 5.0.x, 6.0.x, and **10.0.301**. Note: no 8.0.x SDK is present, but that does not matter since we are moving off net8.0.
- **global.json**: None found anywhere under the repo root (recursive search returned no results). No SDK pin to adjust — roll-forward will select 10.0.301.

No changes required. Toolchain is ready for the .NET 10 upgrade.
