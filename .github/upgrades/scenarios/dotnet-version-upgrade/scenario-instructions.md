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
