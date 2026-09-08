# Decision Record: Screen-Time Usage Tracking

Status: **Accepted** (design finalized, initial implementation in progress)
Related: `docs/ARCHITECTURE.md` (Phase 4 — usage ledger), `docs/STATUS-AND-ROADMAP.md`

## Problem

ScreenBux2 today enforces policy in two ways only:
1. **Process/window rule blocking** (`PolicyRule`) — instant, stateless "is this app allowed right now".
2. **Device grants** (`DeviceGrant`) — a temporary "pause all enforcement" countdown set manually by a parent.

Neither answers "how much has this child actually used today, across all their devices" — the
core "screen time" concept. `AppPolicy.MaxUsageMinutesPerDay` is declared but never enforced, and
is on a dead code path (`PolicyService.ShouldBlockProcess` returns early whenever any `PolicyRule`
exists).

## Goals (in priority order, not all required for v1)

1. Accumulated usage time, per day, per child (across all of the child's devices).
2. Accumulated usage time, per day, per device.
3. Application groups/categories (e.g. Games, Social Media, Other) with independent time budgets
   per group.

Goals 2 and 3 must not require a schema redesign later — the v1 schema is shaped to support all
three from day one, even though only goal 1 (and the device breakdown that falls out of it for
free) is enforced initially.

## Key decisions

### 1. Store aggregates, not raw events

No per-tick/per-event table is written to SQL Server. The durable store is a small family of
**accumulator rows**, upserted with delta increments (`AddMinutes`-style, mirroring the existing
`DeviceGrant`/`IGrantStore.AddMinutesAsync` pattern) — never one row per usage tick. This keeps
row count bounded by `days × children × devices × categories`, not by polling frequency.

Raw, fine-grained detection (`ProcessMonitoringService`'s poll loop, currently every
`CheckIntervalSeconds`) stays exactly as fast as it is today for *enforcement*, but is decoupled
from what gets *persisted*. The Service accumulates elapsed time in memory and only flushes a
delta periodically.

### 2. Accumulator key shape

`UsageDailyTotal`: one row per `(EffectiveDate, ChildProfileId, DeviceId, AppCategoryId)`,
holding an accumulated `Seconds` count (seconds, not minutes, to avoid rounding loss across many
small flushes).

- **Per-day/per-child total** (goal 1) = `SUM(Seconds) GROUP BY EffectiveDate, ChildProfileId`.
- **Per-day/per-device total** (goal 2) = the same rows, grouped/filtered by `DeviceId` instead.
- **Per-category budgets** (goal 3) = the same rows, grouped/filtered by `AppCategoryId`.

No separate table per granularity — every reporting/enforcement view is a projection of the same
accumulator table.

### 3. Application categories

- `AppCategory` (id, name, is-system-default flag) — seeded with a single `"Other"` category so
  the mechanism works identically before a parent ever configures groups.
- `AppCategoryRule` (regex on process name and/or window title → category id) — reuses the exact
  matching approach already proven by `PolicyRule`, rather than inventing a new matching engine.
- Uncategorized/unmatched processes fall into `"Other"`.

### 4. Day boundary: local device time, configurable start hour

- The "day" is anchored to **local device time**, not UTC — a parent/child both experience a
  physical timezone, and a UTC day boundary would be meaningless to them.
- The boundary is **not fixed to midnight**. A per-child `DayStartHour` (0–23, default `0`)
  lets a parent "tilt" the day (e.g. `5` for a 5am–5am day), which better matches how bedtime/
  wake-time restrictions are actually reasoned about for a teenager. Default `0` preserves
  ordinary midnight-based behavior for anyone who doesn't configure it.
- Effective day key: `effectiveDate = localTime.TimeOfDay < DayStartHour ? localDate.AddDays(-1) : localDate`.
- Assumption: a single child's devices share one timezone/clock (a household). Not designed to
  reconcile a child whose devices disagree on timezone.
- **Clock-trust risk**: a device's wall clock is attacker-controlled by the same user the
  system is trying to restrict. Mitigations, in increasing order of rigor (v1 implements the
  first two, later ones are follow-up work):
  1. Elapsed-time deltas within a session use a **monotonic timer** (`Environment.TickCount64`),
	 not a wall-clock diff, so pausing/adjusting the clock mid-session doesn't erase usage.
  2. Every sync flush is reconciled against **server-received time**; a large discrepancy
	 between the device's claimed local time and the server's clock is logged as a signal.
  3. (Follow-up) Stricter response to detected clock skew (reject/clamp the day-key instead of
	 just logging) — deferred until there's real signal on how often this happens in practice.
- DST transitions (23/25-hour days) are a known, accepted minor edge case; duration-based
  accumulation is unaffected, only the day-key computation needs a comment noting this is
  intentional, not a bug.

### 5. Sync cadence: decoupled from enforcement cadence

- **Enforcement** (the "should I block now" decision) checks an **in-memory/local cache** on the
  Service, refreshed every existing poll tick — zero additional DB/network cost.
- **Server sync** (the thing that writes to SQL Server) runs on its own, much slower cadence —
  baseline **every 5 minutes**, not every poll tick — because cross-device budget awareness only
  needs to be eventually consistent; a parental-control feature tolerates a few minutes of
  cross-device drift.
- **Adaptive tightening**: once local accumulation shows a child within ~10–15 minutes of a
  budget, the Service syncs more frequently so other devices see a fresher cross-device total
  right when it matters.
- Resilience mirrors the existing `PolicySyncService`/`GrantService` pattern: flush on graceful
  Service shutdown, and catch up any backlog delta on reconnect after an outage.

### 6. Enforcement independence

Screen-time enforcement is a **third, independent check** in `ProcessMonitoringService` —
evaluated similarly to the existing `DeviceGrant` pre-check — not folded into either the
`PolicyRule` or legacy `AppPolicy` systems, so it can't be silently shadowed by whichever rule
system currently "wins" (see the existing `Rules`-short-circuits-`AppPolicy` gotcha).

## Non-goals for v1

- Enforcing `AppPolicy.MaxUsageMinutesPerDay` (superseded by the new `UsageDailyTotal` +
  `AppCategory` budget model; the old field stays dead/unused until removed in a later cleanup).
- Parent-facing UI for configuring category rules and budgets (server/service plumbing lands
  first; WebClient UI is a follow-up).
- Strict clock-tamper rejection (log-only for now, per decision 4.5.3).
