# Decision Record: Group-Based Policy Model (Groups, Group Policies, Time Windows)

Status: **Accepted (design)** — supersedes/extends `docs/decisions/screen-time-usage-tracking.md`.
No code has changed yet for this decision; it captures the target model before a migration plan
is drafted.

## Problem

`PolicyConfiguration` today has two parallel, disconnected systems:
- `Rules: List<PolicyRule>` — regex matching *plus* a fixed action, baked together, with no
  concept of "mode-dependent behavior" other than duplicating the regex across every
  `PolicyProfile`.
- `Policies: List<AppPolicy>` — legacy, dead (short-circuited whenever `Rules` is non-empty),
  tries to express time windows / daily budgets but at the wrong granularity (per raw app match,
  not per reusable group) and was never wired to any real usage accounting.

Neither lets a parent say "Games are time-limited to 2h in Normal mode, blocked entirely in
School mode" without duplicating the app-matching regex in both modes and hand-authoring a
separate action per duplicate.

## Core decisions

### 1. Split "classification" from "enforcement"

- **`AppGroup`** (mode-independent, account-wide) — a named group of apps (Games, Social Media,
  Other), defined by `AppGroupRule` regex matches (process name and/or window title). This *is*
  the `AppCategory`/`AppCategoryRule` pair already built for usage tracking — no new
  classification system, the two efforts converge on one.
- **`GroupPolicy`** (per `PolicyProfile`/mode, per `AppGroup`) — replaces `AppPolicy` entirely.
  One row per (mode, group): `Enforcement` = `Blocked` | `Allowed` | `TimeLimited`, plus
  `DailyBudgetMinutes` (only meaningful for `TimeLimited`), `Action` (CloseProcess/
  KillProcessTree, only meaningful for `Blocked`/budget-exceeded), and optional `AllowedWindows`
  (see decision 3).
- **`SessionRule`** — the surviving purpose of today's `PolicyRule.Always` kind: a whole-session
  action (Sleep/Hibernate) not tied to any app group. Renamed away from `PolicyRule` once
  `ProcessMatch` rules are retired, to avoid the old name implying per-process matching.

### 2. Usage accounting is foreground-driven, not "all matching processes"

- Only the process/window currently in the **foreground** (per the Agent's existing
  `ForegroundWindowDetector` reports over the named pipe) accumulates time for its classified
  group. Background processes matching a group's rules do not accumulate usage merely by
  running — that would count time nobody experienced.
- Enforcement (closing a `Blocked` group, or a `TimeLimited` group that has exhausted its
  budget) still applies to **every currently running process** matching that group, not just the
  foreground one — accounting is foreground-only, blocking is all-matching.
- This finally gives `UsageDailyTotal` a real per-group source of truth (see
  `docs/decisions/screen-time-usage-tracking.md`), replacing the v1 placeholder that attributed
  everything to "Other".

### 3. `TimeWindow` is one reusable building block, used in two places now, a third later

`TimeWindow { DaysOfWeek: [...], StartTime, EndTime }` (must handle overnight ranges, e.g.
22:00-06:00, as wrapping past midnight — explicit evaluation logic needed, not an edge case to
discover later).

- **On `GroupPolicy.AllowedWindows`** — revives `AppPolicy.AllowedTimeWindows`, but scoped to a
  reusable group instead of a single flat app-match. Outside the configured windows, the group is
  treated as `Blocked` regardless of its nominal `Enforcement` value. Combinable with
  `DailyBudgetMinutes` — e.g. "allowed only Fri-Sun 18:00-22:00, and capped at 1h within that
  window." The daily budget is **one day-wide total**, not reset per-window occurrence — a
  window merely gates *when* the group is permitted at all; it doesn't multiply the budget.
- **On `SessionRule.Schedule`** — makes `Always` conditional: no schedule = literally always true
  (today's behavior, preserved for backward compatibility); a schedule present = only true
  while current device-local time falls in one of the windows. Answers "bedtime lockout" needing
  a trigger.
- **(Deferred, not part of this pass) On `PolicyProfile` itself** — auto-selecting the active
  mode by schedule (e.g., "School" active automatically Mon-Fri 08:00-15:00) instead of only
  manual parent toggling. Explicitly a **non-goal for this migration** — the grid/group model
  doesn't need to anticipate its shape, since it only adds a "which profile is active right now"
  resolution step in front of everything else. Manual override always wins when set.

### 4. Enforcement precedence (per foreground-classified process, per tick)

1. Any `SessionRule` (schedule-gated `Always`) currently firing → short-circuits everything else
   (existing behavior, e.g. Sleep/Hibernate lockout).
2. Classify the process into its `AppGroup` (first matching `AppGroupRule` wins; no match →
   account's system-default "Other" group).
3. If the group's `GroupPolicy` for the active mode has `AllowedWindows` and the current time is
   outside all of them → treat as `Blocked`.
4. Otherwise apply the group's nominal `Enforcement`: `Blocked` → act now; `TimeLimited` → compare
   `UsageDailyTotal` (filtered by child + group + effective day) against `DailyBudgetMinutes` →
   act if exceeded; `Allowed` → no action.

### 5. Device time grants are orthogonal to usage accounting (correction to v1)

A `DeviceGrant` short-circuits **blocking/enforcement only** (step 1 above is skipped, groups are
never checked) — exactly like today. It must **not** pause usage accumulation. The current `v1`
implementation in `UsageTrackingService` incorrectly gates accumulation on
`!_grantService.IsGrantActive`; this is a bug to fix in the migration: a bonus grant is
"permission to keep going," not "this time doesn't count" — usage still needs to show up in
reporting/history even while a grant is active, otherwise granted time becomes invisible,
unaccounted screen time.

## Non-goals for this pass

- Mode-auto-scheduling (decision 3's third bullet) — explicitly deferred, expected to bolt on
  later without reshaping `AppGroup`/`GroupPolicy`/`TimeWindow`.
- WebClient UI changes — grid + per-cell "advanced" schedule editing is a follow-up once the
  server/service model exists.
- Migrating historical `PolicyDocument` JSON — no real production data exists yet; a hard cut is
  acceptable rather than building a compatibility shim for the old `Rules`/`Policies` shape.

## Open items carried into the migration plan

- Exact renaming: keep `AppCategory`/`AppCategoryRule` names (already shipped for usage
  tracking) rather than introducing `AppGroup`/`AppGroupRule` as new types — avoid a rename churn
  for a concept that already exists correctly, unless the name itself is judged confusing.
- Whether `GroupPolicy` lives as a new EF entity keyed by `(PolicyProfileId, AppCategoryId)`, or
  as a JSON sub-document inside `PolicyDocument` like today's `Policies`/`Rules` — leaning
  towards a first-class entity for query-ability (e.g. "which groups are TimeLimited across all
  profiles") but this is a migration-plan decision, not a data-model decision.

## Addendum: `CategoryPolicy.CategoryNames` (multi-category enforcement buckets)

Accepted after the initial migration shipped. Real-world editing revealed that classification
granularity (fine, for reporting - "Games", "Social Media", "YouTube") and enforcement
granularity (coarse, for parenting decisions - "Distraction") are often genuinely different: a
parent wants to see separate usage history per fine-grained category, but wants one shared
`TimeLimited`/`Blocked` rule to apply across several of them at once (e.g. one 2-hour budget
shared by Games + Social Media + YouTube combined), without being forced to either merge those
categories' classification rules or duplicate the same policy three times.

- `CategoryPolicy.CategoryName` (single string) → `CategoryPolicy.CategoryNames` (`List<string>`).
  A JSON array was chosen over a delimiter-encoded string (e.g. `"Games|Social Media"`) for
  consistency with every other multi-value field in this model (`DaysOfWeek`, `AllowedWindows`)
  and to avoid delimiter-escaping edge cases.
- Classification is unaffected: a process still classifies into exactly one fine-grained
  `AppCategoryConfig` via `PolicyService.ClassifyProcess`, and `UsageTrackingService` still
  accumulates/reports usage per fine-grained category name. Only policy *lookup* changes, from an
  exact-name match to a membership check (`CategoryNames.Contains(name)`).
- Budget accounting: for a `TimeLimited` policy governing multiple category names, the budget
  is checked against the **sum** of today's usage across every name in `CategoryNames`
  (`UsageTrackingService.GetTotalSecondsTodayForCategories`), not any single category in
  isolation. The budget itself remains one number, shared across the whole bucket.
- Overlap resolution: if the same category name appears in more than one `CategoryPolicy` on the
  same profile, first-match-wins (in list order) — consistent with the existing first-match-wins
  semantics already used for `AppCategoryRule` classification, so the codebase has one overlap
  rule, not two.
- No back-compat shim: per the "no real production data" non-goal above, saved
  `PolicyProfile.PolicyJson` documents using the old singular `CategoryName` field will need to be
  re-edited (they were already expected to be migration-fragile before general availability).

