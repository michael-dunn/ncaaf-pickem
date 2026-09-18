# 04 - Domain Algorithms

These are the exact rules the pure `Domain` functions implement and the tests assert. When a story and this file disagree, the story wins and this file must be fixed. Each section names the Domain type that owns it and the test class that proves it.

## 1. Season calendar (Feature 13)

Owner: `Seasons/SeasonCalendar`. Tests: `SeasonCalendarTests`.

- `Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York")`.
- `ToEastern(utc)` and `ToUtc(easternLocal)` are the only conversions used anywhere.
- `IsSaturdayEastern(kickoffUtc)` = `ToEastern(kickoffUtc).DayOfWeek == Saturday`. A Friday 11:30 PM Pacific kickoff is Saturday 2:30 AM Eastern and counts.
- Week windows come from `SeasonWeeks` (provider week numbers). `StartUtc` = Sunday 00:00:00 ET, `EndUtc` = Saturday 23:59:59.999 ET, both converted with DST-aware `ToUtc`.
- `CurrentWeek(nowUtc, season)` = the week whose `[StartUtc, EndUtc]` contains now. Before the first week's start = first week (not pickable yet: "season starts"). After last regular-season week end = last week with `IsSeasonOver = true`.
- A game belongs to its provider week regardless of when it finishes.
- League range: `FirstWeek` default 1 (never 0 by default), `LastWeek` default = max week with `IsRegularSeason = 1`. Both clamped to regular-season weeks.
- `LeagueIsComplete(nowUtc)` = now > `SeasonWeeks[LastWeek].EndUtc`.

## 2. Game set generation (Feature 02)

Owner: `GameSets/GameSetGenerator`. Tests: `GameSetGeneratorTests`. Input: league rules for the week (default or override), all `Games` for (season, week), rankings for (season, week, AP), current set rows (for manual adds/removes). Output: list of `(GameId, Source)` plus `ExceedsMax`.

The generator is pure and takes flattened records, not entities: `GameSetGenerationRequest { Week, Games: GameInfo[], Rules: RuleInfo[], Rankings: RankingSet[], ExistingGames: ExistingSetGame[], IsLocked }` in, `GenerationResult { Games: GeneratedGame[], Added, Removed, RemovedIneligible, ExceedsMax, UsedFallbackRankings, LockAtUtc, IsLocked }` out. The service projects rows into them and decides what to persist, raise, and refuse.

1. Eligible pool = games where `IsSaturdayEastern`, both teams `Classification == FBS`, `Status not in (Postponed, Cancelled)`. The pool gates every source, manual adds included: the story excludes non-Saturday and non-FBS games "regardless of rule" and requires cancelled or postponed games out of the set even when they are already in it.
2. For each rule, select from the pool:
   - `Top25`: either team has an AP rank for this week. Use the rankings row set with the latest `FetchedUtc` for the week; if none for this week, fall back to the most recent prior week's poll and flag `UsedFallbackRankings`.
   - `Conference(C, conferenceGamesOnly)`: if `conferenceGamesOnly`, both teams in C; else either team in C.
   - `Team(T)`: T is home or away. Bye week yields nothing.
3. Result = UNION of rule selections (distinct by GameId), `Source = Rule`.
4. Add existing rows with `Source = Manual AND IsRemoved = 0` even if they match no rule, provided the game is still in the step 1 pool. A game that is both a manual row and a rule match keeps `Source = Manual`, so it stays immune to later regeneration.
5. Exclude any GameId whose existing row has `IsRemoved = 1` (sticky removal).
6. `ExceedsMax = Count > 50`. Generation is refused (409) when true; preview reports it. The full list is still returned so the preview can show what the rules produced.
7. Regeneration diff: `Added = new - old active`, `Removed = old active rule-sourced rows the rules no longer select` (manual rows are never in `Removed`), `RemovedIneligible = old active rows of either source whose game left the step 1 pool`. Emit `GameAddedToSet` and `GameRemovedFromSet` events per diff item. Removed rows are marked `IsRemoved = 1` rather than deleted so picks remain, with `RemovedReason = "Rule regeneration"` for `Removed` and `"Schedule change"` for `RemovedIneligible`.
8. After lock (`LockedUtc != null`) generation is a no-op. The domain function returns `GenerationResult.Locked` - `IsLocked = true`, empty games and empty diff - and the caller turns that into the 409; the Tuesday regeneration job just skips the week.
9. `LockAtUtc` = min `KickoffUtc` over active games, or null. Games are returned ordered by `KickoffUtc` then `GameId`, so two runs over the same inputs produce the same list.

Preview runs steps 1 to 7 with candidate rules and without persisting, and ignores lock: only saving is refused after lock.

## 3. Point value resolution (Feature 03)

Owner: `Points/PointValueResolver`. Tests: `PointValueResolverTests`.

```
Resolve(game: PointGameInfo, pointValueOverride?, leagueDefault, rules: PointRuleInfo[], currentSpread?):
  if pointValueOverride != null  -> (override, Source = Override)
  for rule in rules sorted by Priority asc:
    if Matches(rule, game, currentSpread) -> (rule.PointValue, Source = Rule, MatchedRuleId)
  -> (leagueDefault, Source = Default)
```

The resolver takes slim input records, not entities (P3-02): `PointGameInfo(HomeTeamId, AwayTeamId, HomeConferenceId?, AwayConferenceId?, IsConferenceGame)`, `PointRuleInfo(Priority, RuleType, ConferenceId?, TeamId?, SpreadThreshold?, PointValue, RuleId?)`, returning `PointResolution(Value, Source, MatchedRuleId?)`. The caller passes `setGame.PointValueOverride`, `league.DefaultPointValue`, and the newest `GameLines.Spread`. `Resolve` sorts by `Priority` itself, so an unsorted list still resolves correctly; ties keep input order.

`Matches`:
- `ConferenceGame(C?)`: `game.IsConferenceGame` and (C is null or both teams in C).
- `CloseSpread(threshold)`: `currentSpread != null && Math.Abs(currentSpread) < threshold`. No spread = no match; a spread exactly equal to the threshold = no match.
- `Team(T)`: T is home or away.

A rule missing the field its type needs (a close-spread rule with no threshold, a team rule with no team) never matches; `PointRuleValidation` is what reports that, and it is the only gate on the 1..100 range. `Resolve` returns whatever value it is handed.

Rules:
- `ResolvedPointValue` is recomputed for every active game in every unlocked week whenever league default, point rules, or an override change, and on every generation.
- `IsPointValueElevated = PointValueResolver.IsElevated(ResolvedPointValue, league.DefaultPointValue)` = `ResolvedPointValue > league.DefaultPointValue`. Equal to the default is not elevated.
- At lock, `SpreadAtLock` = current spread and `ResolvedPointValue` is computed one final time, then frozen. Nothing after lock may change it. Values 1..100 only.

## 4. Picks and submission status (Feature 04)

Owner: `Picks/SubmissionStatusCalculator`. Tests: `SubmissionStatusTests`.

Given the member's picks over active games in the set, before lock:
- `TotalCount` = active games. `PickedCount` = picks on active games.
- `PickedCount == 0` -> `NotStarted`.
- `0 < PickedCount < TotalCount` -> `InProgress`.
- `PickedCount == TotalCount` and member has pressed Submit since the last time TotalCount increased -> `Submitted`; otherwise `InProgress` (this is how a newly added game reverts a Submitted member).
- Changing a pick while `Submitted` keeps `Submitted` (`SubmittedUtc` stays).
- A game removed from the set: its pick row is kept but ignored; if the member was `Submitted` they stay `Submitted`.

At lock (job): for each active membership at lock time, `Submitted` -> `Locked`; anything else -> `Incomplete`. Members who joined after lock get no row for that week.

Rules enforced server-side, every call: reject pick/submit when `nowUtc >= LockAtUtc` or `LockedUtc != null` (409). Reject pick where `TeamId` is not home/away of the game (400). Reject picks on removed or voided games (409). Tapping the already-picked team is a no-op success.

Visibility: `/picks` (all members) and `/grid` return 403 until `LockedUtc != null`.

## 5. Lock job (Features 04, 05)

Owner: `Infrastructure/Jobs/LockWeekJob` calling `Domain/Picks/WeekLocker`. Tests: `WeekLockerTests`, `LockWeekJobTests`.

Runs every minute on Saturdays (and Friday night for early-Saturday-ET kickoffs). For each `WeekGameSets` with `LockAtUtc <= now AND LockedUtc IS NULL`:
1. Snapshot: for each active game set `SpreadAtLock` and final `ResolvedPointValue`.
2. Set statuses per section 4.
3. Set `LockedUtc = now` (not LockAtUtc, so late runs are visible in logs).
4. Emit `WeekLocked`.

Idempotent: a second run finds `LockedUtc` set and skips.

## 6. Influence dashboard (Feature 05)

Owner: `Dashboard/InfluenceCalculator`. Tests: `InfluenceCalculatorTests` including the Overview worked example verbatim (Michael, Alyson, Dance, Alex, Daniel).

Input: viewer membership M, active games in the locked set, all picks by active-at-lock members (former members who were active at lock are included; members who joined after lock are excluded), game statuses.

For each game G:
- `MyTeam` = M's pick or null.
- `OppositePicks` = members (not M) whose pick is the team M did not pick. If M has no pick, `OppositePicks` is empty and the DTO carries both team's pickers in `NoPick`-adjacent fields as "Picks for both teams" (UI shows both lists); `MyOutcome = NoPick`.
- `NoPick` = members (not M) with no pick on G.
- `OppositeCount = OppositePicks.Count`.
- `SwingPoints = PointValue * OppositeCount` (informational).
- `MyOutcome`: `Pending` unless Final: `Won` if winner == MyTeam, `Lost` otherwise; `NoPick` if no pick.

Ordering of `Games`: `OppositeCount desc`, then `PointValue desc`, then `KickoffUtc asc`. Games with `OppositeCount == 0` go to `EveryoneAgrees` (same secondary ordering) unless `MyOutcome == NoPick`, which stays in `Games`.

Header: `PointsSoFar` = sum of PointValue over Final games where Won. `MaxRemaining` = sum of PointValue over non-Final, non-voided games where M has a pick.

Winner determination: `ResultOverrideWinnerTeamId` if set, else higher score when `Status == Final`. Tie or missing scores when Final = no winner (flag for review, treat as Pending in the dashboard).

## 7. Scoring (Feature 06)

Owner: `Scoring/WeekScorer`. Tests: `WeekScorerTests`.

Trigger: `GameWentFinal`, `ResultOverridden`, `GameVoided`, and a nightly full recompute for safety.

`ScoreWeek(set, picks, memberships)` recomputes `WeekResults` for every membership that was active at lock (has a `WeekSubmissions` row) from scratch, then upserts. Because it is a full recompute from source rows, it is idempotent.

Per member: for each active game with a determinable winner: `Points += PointValue` and `CorrectCount++` if pick == winner. Voided games contribute nothing and are excluded from `ActiveGameCount`. `IsWeekComplete` = every active game is Final with a winner (or voided). When a week first becomes Complete, write `SeasonStandingsSnapshots` for `ThroughWeek = week` (section 8) and emit nothing else.

Ties/no winner: game stays unscored (0 to everyone) and appears in the data status page under "needs review" until overridden or voided.

Delayed games: scored whenever they go Final; week number is the provider's, so post-midnight finishes land correctly.

Overrides and voids are allowed only after lock. Both write `AuditLog` and trigger a rescore. Voided games render as "Voided" in grids and history.

## 8. Leaderboard (Feature 07)

Owner: `Leaderboard/StandingsCalculator`. Tests: `StandingsCalculatorTests`.

Season rows: active memberships only (RemovedUtc null). `TotalPoints` = sum of `WeekResults.Points` across the league's weeks. Rank uses competition ranking ("1224"): equal totals share a rank, next rank skips. `PointsBehind = leaderTotal - TotalPoints`. `WeeklyWins` = count of Complete weeks where the member's points equal that week's max among members with a result row (ties share the win).

Trend: compare the member's rank in `SeasonStandingsSnapshots(ThroughWeek = latest Complete week)` with `ThroughWeek = previous Complete week`. Lower rank number = `Up`. Missing previous snapshot (first Complete week, or member joined since) = `None`.

Week rows: every membership with a `WeekResults` row for that week, including former members (`IsFormer`). Rank by `Points` with competition ranking. `IsWinner` = Points == max and week `IsComplete`. Weeks not Complete are labeled provisional by the client using `IsComplete`.

Grid: rows = active games ordered by kickoff (voided included, flagged), columns = memberships with a submission row for the week. Cell outcome: `Voided` > `NoPick` (no pick row) > `Pending` (no winner yet) > `Correct` / `Incorrect`.

## 9. Provider matching (Feature 12)

Owner: `Infrastructure/Providers/GameMatcher`. Tests: `GameMatcherTests` with fixture payloads.

- CFBD is the source of truth for `Games`. ESPN supplies status, scores, period, clock.
- Match an ESPN event to a `Games` row by `(KickoffEasternDate, normalized home name, normalized away name)`, where normalization = lowercase, strip punctuation, strip "university", "state" kept, apply `TeamAliases(Source=Espn)`, then compare against `Teams.School`, `Teams.Abbreviation`, and aliases. Once matched, store `EspnEventId` and `EspnTeamId` so future polls match by ID first.
- Unmatched ESPN events for FBS (`groups=80`) on a Saturday where a CFBD game exists for the same date with no `EspnEventId` are written to `UnmatchedGames` once and surfaced on the data page.
- Status mapping: ESPN `status.type.name`: `STATUS_SCHEDULED` -> Scheduled, `STATUS_IN_PROGRESS`/`STATUS_HALFTIME`/`STATUS_END_PERIOD`/`STATUS_DELAYED` -> InProgress, `STATUS_FINAL` -> Final, `STATUS_POSTPONED` -> Postponed, `STATUS_CANCELED` -> Cancelled. `completed == true` also implies Final.
- A game transitions to Final only when the feed says Final; `GameWentFinal` fires once per game (guard on previous status).

## 10. Saturday poller cadence (Features 06, 09, 12)

Owner: `Infrastructure/Jobs/SaturdayPoller`. Tests: `SaturdayPollerScheduleTests`.

- Active window per Saturday (ET): from `min(LockAtUtc across leagues) - 5 min` until every active game in every active league's set is Final/Voided/Postponed/Cancelled, or 3:00 AM ET Sunday, whichever first.
- Cadence 5 minutes with ESPN. If `Providers__LiveScores = Cfbd`, cadence 10 minutes. Outside the window: no calls.
- On 3 consecutive ESPN failures, switch to CFBD fallback for the rest of the day and set `ScoresMayBeStale = true` on dashboards; log a warning. Reset next Saturday.
- Every call recorded in `ProviderCalls`. Monthly CFBD count exposed; warn at 800.

## 11. Notification scheduling (Feature 11)

Owner: `Infrastructure/Jobs/ReminderJobs`. Tests: `ReminderJobTests`.

- Cron evaluated in Eastern: Friday 20:00 (member reminder), Friday 21:00 (commissioner summary), and a per-week one-shot at `LockAtUtc - 1h` (Saturday reminder) recomputed whenever `LockAtUtc` changes.
- Recipients evaluated at send time. Skip members with status Submitted. Skip everything if the league has no game set for the current week or the week is already locked.
- Once-per-week guarantee via the `NotificationLog` filtered unique index; a duplicate insert = Skipped.
- Event-driven: `GameAddedToSet` (only to members who were Submitted) and `GameRemovedFromSet` (only to members with a pick on that game) send immediately, coalescing multiple adds in one regeneration into one "N new games" message.
- Delivery: 404/410 deletes the subscription (Result = Expired). Other failures retry 3 times over 15 minutes with backoff, then Failed.
