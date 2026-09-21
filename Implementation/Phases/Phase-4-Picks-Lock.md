# Phase 4 - Picks and Lock

Stories: 04 Weekly Picks, 05 (lock portion), 13 (current week rollover).
Depends on: Phase 3b (game sets exist), P0-06 (scheduler).

Read first: WorkItems 04, `04-Domain-Algorithms.md` sections 4 and 5, `03-API-Contracts.md` (Picks), `02-Data-Model.md` (Picks).

---

## P4-01 Picks service and endpoints
Tier: Opus. Depends on: P3-03.

Deliverables
- `Domain/Picks/SubmissionStatusCalculator` per `04` section 4, pure.
- `PickService`: set pick (validate team in game, not locked, game active), submit (all active picked, not locked), ack-changes, status recompute on every change, `HasUnseenGameChanges` maintenance.
- Endpoints per contracts: my picks GET, set pick PUT, submit POST, ack-changes POST, all picks GET (403 before lock), status roster GET (Commish).
- Current-week guard: picks accepted only for the current week per `SeasonCalendar` and only for the league's range; older weeks 409.
- Member view of past weeks: my picks GET works read-only for any generated week.

Done when
- `SubmissionStatusTests` (all transitions in `04` section 4), `SetPickTests` (valid, wrong team 400, re-tap no-op, removed game 409), `SubmitTests` (incomplete 409, complete ok, change after submit stays Submitted), `PicksVisibilityTests` (403 before lock, 200 after), `LockEnforcementTests` (409 at and after `LockAtUtc` even if the lock job has not run), auth matrix.

## P4-02 Lock job
Tier: Opus. Depends on: P4-01, P0-06.

Deliverables
- `WeekLocker` (domain) and `LockWeekJob` (one-shot per week via `IOneShotScheduler`, also swept every minute Friday 18:00 to Sunday 03:00 Eastern) per `04` section 5: snapshot spreads and point values, set Locked/Incomplete statuses, `LockedUtc`, raise `WeekLocked`.
- Refuse all config mutations (game set, points, overrides) once `LockedUtc` is set (already 409 in P3-03; add tests here that go through the job).

Done when
- `WeekLockerTests`, `LockWeekJobTests` (idempotent second run; snapshot values frozen; Incomplete for partial pickers; late-joiner has no row), plus a test that the picks endpoint and config endpoints all 409 after the job runs.

## P4-03 Picks UI
Tier: Sonnet. Depends on: P4-01 contracts; integrate after merge.

Deliverables (`Pages/Picks/PicksPage.razor` at `/leagues/{id}/weeks/{week}/picks`)
- Header: "Week N", status pill (Not Started / In Progress n of m / Submitted / Locked / Incomplete), lock countdown in local time with Eastern hint.
- Game cards ordered by kickoff: two large team buttons (logo, rank, name) each at least 44px tall, kickoff local time, point badge (highlighted when elevated), selected state obvious. Tapping calls PUT immediately; optimistic UI with a small "Saved" tick; on failure show inline error and revert that card.
- New-since-submit games get a "New" ribbon; page load calls ack-changes after render.
- Sticky footer: "Submit" enabled only when all picked, otherwise "N picks left". After lock, footer shows "Locked" and cards are read-only.
- Past weeks: read-only with correct/incorrect coloring when results exist (uses `WinnerTeamId`).
- Performance: page fetches one `MyPicksResponse`; no per-card requests on load.

Done when
- Screenshots at 375px: empty, in progress, submitted, locked, past week. Offline simulation shows revert. Interactive within 2 s on repeat load (measure with the P0-04 method and record).

## P4-04 Game added/removed handling for picks
Tier: Sonnet. Depends on: P4-01, P3-04.

Deliverables
- Handlers for `GameAddedToSet` and `GameRemovedFromSet`: recompute submission status for every membership in the league week (Submitted -> InProgress when a new active game appears), set `HasUnseenGameChanges = true` for affected members, keep pick rows on removed games.
- Roster status endpoint reflects changes immediately.

Done when
- `GameAddedTests` (Submitted member reverts, NotStarted unchanged, flag set), `GameRemovedTests` (pick retained, status unchanged, flag set for members with a pick on it).

## P4-05 Spread on the picks page
Tier: Fable. Depends on: P4-01, P4-02, P4-03. Added 2026-09-21 at the owner's request, after Phase 10.

Deliverables
- `GameSetGameDto.Spread?` (home-relative, D-181): newest `GameLines` row while the week is open, `SpreadAtLock` once locked, null when no line.
- `SpreadDisplay` in `Shared/Contracts/GameSets` and a line beside the kickoff on every `PickGameCard`.

Done when
- `SpreadDisplayTests`, `PicksSpreadTests` (newest line on `GET .../picks/me`), and `LockWeekJobTests` (a late line is stored but a locked week still shows the frozen one) pass.

---

## Phase exit criteria
- Demo league members can pick, submit, and change picks against the week 7 fixture; lock job locks at the fixture's earliest kickoff; all mutations 409 afterward; all-picks endpoint opens after lock.
