# Phase 11 - The simulator: a separate test-bench website, real time everywhere

Status: Implemented 2026-09-28 (P11-01 on `p11-01-simulator`). Supersedes Phase 10's dev clock.

Goals (from the owner):
1. The dev tools for simulating picks, the dashboard and time live in **their own project**, not in
   the app. A separate website with a dashboard.
2. From that dashboard: set up fake games, scores, kickoff times and spreads, and make picks for the
   other players in the league by hand.
3. Instead of changing the clock, move **when the lock happens and when games are**, so a week is
   not tied to a particular day or time.
4. Be able to test the app's capabilities in full, quickly, on any day at any time.

Depends on: Phase 10 merged (it is). This repo only.

---

## What was wrong with the Phase 10 approach

Phase 10 moved the app's clock (`DevTimeProvider`) into the one fixture week (Week 7, 2026). It
worked, but it was the wrong lever:

- **Two clocks disagree.** The server believed it was October; the browser did not. The lock
  countdown, "last updated" and every relative time in the client were wrong while shifted (D-180
  said so and deferred it).
- **One week of data.** The fixture set has games in exactly one week, so the demo could never get
  past week 7, never build a season, and never show a trend arrow from real play.
- **The schedule was fixed.** The lock was always Saturday noon ET of that week, so testing the
  lock meant jumping the clock to it; testing it again meant jumping back and resetting.
- **The tools were part of the app**: a page in the PWA, five routes in the API, a scoped service
  in Infrastructure, and a CLI branch in `Program.cs`, all of which had to be kept out of
  Production by environment checks.

## Design

**The app always runs on real time.** `TimeProvider.System` everywhere; `DevTimeProvider`, the
clock routes, the demo-week routes, the fixture snapshot route, the Dev tools page and `simulate`
are all deleted. (Tests keep their own pinned clocks, as before.)

**The simulator plays the outside world, not the clock.** `tools/NcaafPickEm.Simulator` is a
separate Blazor Server website that shares the app's database and plays every part the app does not
own:

| Real world | The simulator |
|---|---|
| CFBD: teams, conferences, calendar, schedule, rankings, lines | writes those tables directly, as the ingest would |
| ESPN: live scores | feeds `LiveScoreUpdate`s through `SaturdayPoller.PollOnceAsync`, the poller's own path |
| The other members | makes and submits their picks through `PickService` |
| The calendar ("it is now week 3") | lays out `SeasonWeeks` so the week under test contains *now* |
| The scheduler's clock ("it is Friday 8 PM") | runs any of the app's jobs on demand |

**Everything is expressed relative to real now.** "Lock in 10 minutes" moves the slate's first
kickoff to now + 10 minutes (the lock is the first kickoff, 04-Domain-Algorithms section 2.9), and
the rest of the slate keeps its spacing. The app's own `LockWeekJob` then locks the week within a
minute, the browser countdown is right because both clocks agree, and the games play out in real
time (optionally compressed, see autoplay).

**The calendar follows the tester.** `SeasonWeeks` for the simulated season are rewritten so the
week under test started when the tester entered it and runs seven days from then; earlier weeks
are laid back-to-back behind it, later weeks after it. "Start next week" is a calendar rewrite,
so a whole season can be played on a Tuesday afternoon. The app reads the calendar from the
database in Simulator mode (`DbSeasonWeekSource`, a two-second cache).

**Saturday is a flag, not a day.** `Games.IsSaturdayEastern` is stored, and set-generation reads
the stored flag. Simulated games carry `true` unless the tester marks one as an off-day game to
test the exclusion rule. The same goes for FBS: the catalog has FCS teams for the exclusion rule.

**Everything the app would do goes through the app's services**: `GameSetService` (generation),
`PickService` (picks, submit, status), `LockWeekJob` (lock), `SaturdayPoller.PollOnceAsync` +
`LiveScoreApplyService` (scores, `GameWentFinal`, `GameScheduleChanged`), `ScoringService`,
`InviteService` (joining), and every `IScheduledJob`/`IOneShotJob` for "run now". Domain events are
raised and handled in the simulator's process, so scoring, submission-status reverts and
notifications happen exactly as they would in the app. Only provider-owned rows (the ones an ingest
writes) are written directly.

**Isolation.** The simulator runs against its own database (`NcaafPickEm_Sim` by default) and
refuses a database it did not create (one with leagues or users but no `sim.Settings` table). The
app runs against that database in a new provider mode, `Providers__ReferenceData=Simulator` and
`Providers__LiveScores=Simulator`, which is Development/Testing only and in which the app does not
ingest reference data or poll scores - the simulator owns both. The simulator is under `tools/`,
so the Dockerfile never copies it and the production image cannot contain it.

**The simulator's own state** (which user is "you", autoplay on/off, game length, each game's
target score and autoplay flag) is one JSON document in `sim.Settings`, in the same database as the
data it describes, so a dropped database takes both.

### Autoplay

A background loop in the simulator ticks every few seconds. For every scheduled or live game in the
current week it computes progress = (now - kickoff) / game length and applies the score a
deterministic plan gives at that progress: Q1-Q4 with a clock, scoring in 7s, 3s and the odd 2 or
8, Final at the end. Each game has a target final score (seeded from the spread, favourite usually
wins, never a tie) that the tester can overwrite at any time - including with a tie, to exercise
the "needs review" path. Changing the target mid-game re-plans only the rest of the game, so scores
never go backwards. Touching a game by hand turns autoplay off for that game.

## P11-01 Simulator, Simulator provider mode, removal of the in-app dev tools

**Scope - the app**
- Delete `Infrastructure/Time/DevTimeProvider.cs`, `Infrastructure/Seeding/DemoWeekService.cs`,
  `Api/Endpoints/DevClockEndpoints.cs`, `DemoWeekEndpoints.cs`, `FixtureAdminEndpoints.cs`,
  `Api/Simulation/*`, `Shared/Contracts/Dev/*`, the Web Dev tools page, its API client and fake,
  and the More-menu link. `AddInfrastructure` registers `TimeProvider.System` everywhere.
- `Providers:ReferenceData=Simulator`: `DbSeasonWeekSource` with a 2 s cache; an
  `IReferenceDataProvider` that refuses every call ("owned by the simulator"), so a manual refresh
  from the data page records a readable failure and writes nothing; no reference refresh jobs.
- `Providers:LiveScores=Simulator`: `LiveScoreSource.Simulator`; an `ILiveScoreProvider` that
  returns nothing; no `SaturdayPoller`.
- Both refuse to start outside Development/Testing.
- `/auth/dev-login` also signs in `sim:<name>` users (the simulator's players).
- `launchSettings.json` gets a `simulator` profile (Simulator providers, `NcaafPickEm_Sim`).

**Scope - the simulator (`tools/NcaafPickEm.Simulator`)**
- Engine: `SimCalendarLayout` (pure), `TeamCatalog` (embedded FBS + a few FCS teams), `SlateBuilder`
  (pure), `ScorePlan` (pure), `SimStateStore`, `SimDatabaseGuard`, the `Simulator` facade
  (one gate, one DI scope per step), `GameDirector` (autoplay loop), `SimScoreFeed` (the
  simulator's `ILiveScoreProvider`).
- Dashboard: sandbox setup and wipe; calendar (before season, start week N, next week, play out
  N weeks instantly, season over); lock (in N minutes, at a time, now; rewind keeping picks; reset
  the week); slate (quick slate, add game, per-game kickoff, spread, conference and Saturday
  flags, rankings); scoreboard (kick off, score, quarter, final, tie, postpone, cancel, reopen,
  target score, autoplay); leagues and players (pick grid for everyone, fill strategies, submit,
  clear, add a player, join by invite code, open the app as anyone); jobs (every scheduled and
  one-shot job, run now); activity log.

**Done when**
- [x] The app has no clock, demo or fixture-snapshot routes and no Dev tools page; Production and
      Development both read real time.
- [x] A sandbox created from the simulator on any day makes the app (Simulator mode) report the
      simulated week as current, with a set generated and a lock minutes away.
- [x] Picks made in the simulator are the ones the app shows; the app's lock job locks the week;
      autoplay drives the dashboard to a scored week and a leaderboard.
- [x] Next week / play out N weeks build a season with trend arrows.
- [x] Tests: `SimCalendarLayoutTests`, `ScorePlanTests`, `SlateBuilderTests`,
      `SimulatorScenarioTests` (engine against SQL), `AppInSimulatorModeTests` (the real app over
      the simulator's database, signed in through dev-login), `SimulatorModeTests` (Api).
- [x] `dotnet build -c Release` 0 warnings; `dotnet format --verify-no-changes` clean; all suites green.

## Phase exit criteria
- P11-01 merged; STATUS row Done with the merge commit; D-182..D-186 in DECISIONS.
