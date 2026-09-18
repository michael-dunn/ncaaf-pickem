# Phase 3 - Game Sets and Point Values

Stories: 02 Weekly Game Set Configuration, 03 Point Values, 13 (schedule changes).
Split: **3a** (P3-01, P3-02) is pure domain and starts right after Phase 0, in parallel with Phases 1 and 2. **3b** (P3-03 to P3-05) needs Phase 1 (league membership) and P2-05 (fixtures).

Read first: WorkItems 02, 03, `04-Domain-Algorithms.md` sections 2 and 3, `02-Data-Model.md` (League configuration), `03-API-Contracts.md` (Game set configuration, Point values, Season calendar).

---

## P3-01 Game set generator (domain)
Tier: Opus. Depends on: P0-05.

Deliverables
- `Domain/GameSets/GameSetGenerator` implementing `04` section 2 exactly, as pure functions over in-memory inputs (`IReadOnlyList<GameInfo>`, rules, rankings, existing set rows). Returns `GenerationResult { Games, Added, Removed, ExceedsMax, UsedFallbackRankings, LockAtUtc }`.
- `Preview(rules, ...)` sharing the same core.

Done when
- `GameSetGeneratorTests`: union/distinct, Top25 current poll and fallback, conference either/both, team bye, Saturday-Eastern filter (Friday-Pacific included, Friday excluded), FCS excluded, postponed/cancelled excluded, 50 cap, sticky manual removal, manual add kept, regeneration diff never removes manual rows, no-op after lock, `LockAtUtc` = earliest kickoff.

## P3-02 Point value resolver (domain)
Tier: Opus. Depends on: P0-01.

Deliverables
- `Domain/Points/PointValueResolver` per `04` section 3. Pure.
- `Domain/Points/PointRuleValidation`: values 1..100, threshold > 0, priority unique.

Done when
- `PointValueResolverTests`: default, single rule, priority conflict, close spread with/without spread, override beats all, conference rule with and without a specific conference, elevated flag.

## P3-03 Configuration endpoints and services
Tier: Sonnet (Opus review). Depends on: P3-01, P3-02, P1-01, P2-05.

Deliverables
- `GameSetService`: load inputs from DB, call generator, persist `WeekGameSets`/`WeekGameSetGames` (mark removed rows, insert added), recompute `ResolvedPointValue` for all active games via the resolver, set `LockAtUtc`, raise `GameAddedToSet`/`GameRemovedFromSet`, refuse when locked (409) or `ExceedsMax` (409).
- `PointRuleService`: save rules/default/override and re-resolve every unlocked week of the league.
- Endpoints per contracts: default rules GET/PUT, week rules GET/PUT (UsesOverride), preview, generate, manual add (validates Saturday-Eastern, FBS, cap, not locked), manual remove, member gameset GET, candidate games search, reference conferences/teams, point rules GET/PUT, per-game override PUT.
- Audit: GameManuallyAdded, GameManuallyRemoved.

Done when
- `GameSetRulesEndpointsTests`, `GameSetGenerateTests` (generate, regenerate diff, 409 locked, 409 over cap), `ManualAddRemoveTests`, `PointRulesEndpointsTests` (re-resolve on change, keeps picks intact, 409 override after lock), auth matrix.

## P3-04 Auto-regeneration job and schedule-change handling
Tier: Sonnet (Opus review). Depends on: P3-03, P2-04.

Deliverables
- `RegenerateGameSetsJob` Tuesday 03:30 Eastern (after rankings/schedule refresh): for every active league and its current-or-next unlocked week, regenerate via `GameSetService`. Also runs on the current week when a week override is saved.
- Handler for `GameScheduleChanged` (from P2-03): before lock, remove the game from any set containing it with `RemovedReason = "Schedule change"` and raise `GameRemovedFromSet`; after lock, leave it to Phase 5's void flow (raise `GameNeedsVoidReview` so the data page lists it).
- Create the week's `WeekGameSets` row automatically when the week becomes current and none exists (using default rules), so members always have a set without commissioner action.

Done when
- `RegenerationJobTests` (Tuesday run adds a newly ranked team's game, removes a postponed game, keeps manual rows), `ScheduleChangeTests` (before lock removes and emits; after lock emits review event only), `AutoCreateWeekSetTests`.

## P3-05 Commissioner configuration UI and member week view
Tier: Sonnet. Depends on: P3-03 contracts; integrate after merge.

Deliverables (Blazor `Pages/GameSets/`, `Pages/Points/`)
- **Game set rules** (`/leagues/{id}/config/games`): list of rules as cards; add-rule sheet with segmented control (Top 25 / Conference / Team), conference multi-select with "conference games only" toggle, team search; week selector with "Override this week" toggle; **Preview** button showing matchups (away @ home, ranks, kickoff local), count, red warning when over 50; **Save and generate**.
- **This week's games** (`/leagues/{id}/config/week/{week}`): generated list with remove (x) per game, "Add game" search over Saturday FBS candidates, point value per row with tap-to-override (numeric stepper 1..100, clear), lock time and locked state; all controls disabled when locked.
- **Point rules** (`/leagues/{id}/config/points`): default value stepper; ordered rule list with up/down buttons (no drag); add-rule sheet per type.
- **Member week view** (`/leagues/{id}/weeks/{week}/games`): read-only list ordered by kickoff with ranks, local time, point badge when elevated.

Done when
- Screenshots at 375px for all four pages; every interaction is tap-only; locked state verified with a fixture week that is locked.

---

## Phase exit criteria
- Demo league gets a generated week 7 set from fixture data via default rules, preview and override work, point values resolve with an elevated badge visible, and the Tuesday job regenerates without touching manual rows.
