# Decision Record: Mode Scheduling and Temporary Mode Overrides

Status: **Accepted (design)**. Phase 1 (Shared model + resolver + tests) in progress.
Related: `docs/decisions/group-based-policy-model.md` (decision 3, third bullet, deferred
"auto-selecting the active mode by schedule" - this record is that follow-up).

## Problem

Modes (`PolicyProfile`: Normal, School, Open, Sleep, ...) can only be switched manually, and a
switch is permanent until the parent switches back. Parents want two things:

1. **Temporary switch** - "Open for 1 hour", "Normal until School starts" - that goes back
   automatically, without the parent having to remember to switch back.
2. **Schedule** - which mode is active by default at a given time of week, e.g. School
   Mon-Fri 08:00-15:00 and Sleep every night 21:00-07:00.

Both are the same question - "which mode is in effect right now" - so they are designed as one
resolution step in front of everything else, not two separate features.

## Core decisions

### 1. Three layers, evaluated in order

For each scope (account default, or one child), the mode in effect is:

1. **Override** - a temporary switch, if one is set and has not ended.
2. **Schedule** - the first enabled schedule entry whose `TimeWindow` matches right now
   (first-match-wins, consistent with `AppCategoryRule` and `CategoryPolicy` overlap rules).
3. **Default mode** - what applies when nothing above does. This is what today's "Activate"
   button sets.

"Goes back automatically" needs no extra logic: when an override ends, the schedule/default
layers simply take over again. The child returns to what *should* be active at that moment, not
necessarily to what was active before the override (e.g. "Open for 1h" started at 14:30 during
School ends at 15:30 in Normal, because School ended at 15:00).

Consequences, explicitly accepted:

- **The schedule beats the default.** Changing the default mode during a scheduled slot has no
  visible effect until the slot ends. The UI must say so ("School is scheduled until 15:00 -
  also switch now?"). With no schedule configured, changing the default behaves exactly like
  today's "Activate".
- **An override beats the schedule, including Sleep.** "Open for 1h" at 20:30 stays Open until
  21:30 even though Sleep is scheduled from 21:00.

### 2. Override end conditions

An override (`ModeOverride`) has a `ProfileId`, `StartedAtUtc`, and any combination of:

- `ExpiresAtUtc` - a fixed end ("for 30 min / 1h / 2h", "until 18:00"). Absolute UTC timestamp,
  same pattern as `DeviceGrant`, so "has it ended" is a stateless comparison.
- `EndsAtNextScheduleChange` - ends the first time the schedule/default layers (the
  "underlying" mode) change after `StartedAtUtc`. E.g. "Open until bedtime".
- Neither set - **pin**: lasts until the parent ends it, and ignores the schedule entirely
  (including Sleep). "No school but keep bedtime" is expressed by disabling the School schedule
  entry (`ModeScheduleEntry.Enabled`), not by pinning.

If both end conditions are set, whichever comes first ends the override.

`EndsAtNextScheduleChange` is evaluated **statelessly**: the resolver scans the schedule's window
boundaries between `StartedAtUtc` and now and checks whether the underlying mode changed at any
of them. An earlier idea - recording the underlying mode at start and ending the override when
it differs - was rejected because it is not stateless: an override started at 16:00 (Normal)
would end at 21:00 (Sleep) but come back to life at 07:00 the next morning when the underlying
mode is Normal again. Editing the schedule while such an override runs re-evaluates its end
against the new schedule, which is the desired behaviour.

An `EndsAtNextScheduleChange` override on a scope with no schedule never ends (it behaves as a
pin); the UI should not offer that option when nothing is scheduled.

### 3. Resolution happens on the device, not the server

- All time evaluation is already device-local (`TimeWindow.IsActiveAt(DateTime.Now)` in
  `PolicyService`, `UsageTrackingService`); the server has no time-zone concept.
- If the server did the switching (timer + push), a child could disconnect the network during
  "Open for 1h" and stay in Open indefinitely. Device-side evaluation of absolute expiry closes
  that loophole the same way `DeviceGrant` does.

The device therefore receives a **`DevicePolicyBundle`** instead of a single
`PolicyConfiguration`: the `ModeSelection` (default profile id, schedule, override) already
flattened by the server for that device's scope, plus the `PolicyConfiguration` of every profile
the selection can resolve to. The device runs `ModeResolver` every check tick.

The server runs the **same** `ModeResolver` (in `ScreenBux.Shared`) to show "currently School,
back to Normal at 15:00" in the WebClient. For that, each device reports its time zone
(`Device.TimeZoneId`). The resolver takes the time zone as a parameter rather than using
`TimeZoneInfo.Local`, so both sides - and tests - evaluate identically.

Rejected alternative: server-side timer that switches modes and pushes the result, leaving the
device unchanged. Simpler, but has the offline loophole above and needs a time zone the server
doesn't have.

### 4. Robustness rules in the resolver

- A layer whose profile id is not available (e.g. a profile was deleted but a stale schedule
  still references it) is skipped, falling through to the next layer.
- `StartedAtUtc` does not gate activation (server/device clock skew must not delay an
  override by a few seconds); it is only the starting point for the schedule-change scan.
- The resolver reports *why* a mode is active (`ModeSource`: Override / Schedule / Default) and,
  where it can tell within a bounded look-ahead (8 days), *until when* and *what comes next*.

### 5. Storage (phase 2)

Override and schedule live on `PolicyDocument`, which already exists once per scope (account
default, per child): `OverrideProfileId`, `OverrideStartedAtUtc`, `OverrideExpiresAtUtc`,
`OverrideEndsAtScheduleChange`, and `ScheduleJson` (serialized `List<ModeScheduleEntry>`,
consistent with how `TimeWindow` lists are stored inside policy JSON elsewhere).
`DeleteProfileAsync` must refuse to delete a profile referenced by any schedule or override.

## Unchanged / orthogonal

- **Daily budgets** are tracked per category per day, not per mode; switching mode mid-day
  checks the same running total against the new mode's budget.
- **`DeviceGrant`** stays independent: it pauses enforcement whatever mode is active.
- **`HasSyncedSinceStartup`** safety gate stays as-is for now. With the bundle, offline
  schedule evaluation is trustworthy, so the gate could later be relaxed for scheduled Sleep.
- **Known risk:** changing the device clock affects schedules and overrides - same as existing
  grants and `AllowedWindows`; changing the clock normally requires admin rights.

## Scope

Overrides and schedules per child plus account default. No per-device scope.

## Phases

1. Shared model (`ModeScheduleEntry`, `ModeOverride`, `ModeSelection`, `DevicePolicyBundle`),
   `ModeResolver`, unit tests.
2. Database: `PolicyDocument` columns + `Device.TimeZoneId`, one migration.
3. WebServer: store methods, override/schedule endpoints, bundle endpoint + `PolicyBundleUpdated`
   SignalR push (on any change to a device's bundle), temporary "set everyone to...", delete
   protection.
4. Service: bundle sync/cache, per-tick resolution in `PolicyService`, mode-change notification
   to the Agent, time-zone reporting.
5. WebClient: per-child current mode + "until ..., then ...", "Switch temporarily" with end
   choice, "End now", schedule editor with overlap warning and per-entry on/off.
