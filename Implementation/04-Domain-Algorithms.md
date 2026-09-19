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

Owner: `Dashboard/InfluenceCalculator`. Tests: `InfluenceCalculatorTests` including the Overview worked example verbatim (Michael, Alyson, Dance, Alex, Daniel), driven from `influence-example.json`.

The calculator is pure and takes flattened records, not entities (P6-01, D-095): `InfluenceRequest { ViewerMembershipId, Games: InfluenceGame[], MembersActiveAtLock: InfluenceMember[], Picks: InfluencePick[] }` in, `InfluenceResult { Games, EveryoneAgrees, PointsSoFar, MaxRemaining }` out, where each entry is an `InfluenceGameResult { GameSetGameId, MyTeamId?, MyOutcome, OppositeCount, OppositePicks, NoPick, HomePickers, AwayPickers, SwingPoints, WinnerTeamId? }`. `InfluenceOutcome` (Pending / Won / Lost / NoPick) lives in `Shared/Enums` so the DTO can use it too (D-014).

`MembersActiveAtLock` is the caller's answer, not the calculator's: the service lists the memberships with a `WeekSubmissions` row for the set, which is what includes a member who has since been removed (`IsFormer = true`) and excludes one who joined after lock. The calculator never looks past that list - a pick from an unlisted membership is ignored outright - so a post-lock joiner cannot appear even if their pick rows are handed in.

**Voided games are left out entirely** (D-096): out of `Games`, out of `EveryoneAgrees`, and out of both header totals. Section 6 works over the *active* games in the locked set, and a void is how a game stops being active after lock, exactly as in section 7's scoring.

For each game G:
- `MyTeamId` = M's pick or null.
- `OppositePicks` = members (not M) whose pick is the team M did not pick. If M has no pick, `OppositePicks` is empty and `MyOutcome = NoPick`; `HomePickers` / `AwayPickers` are what the UI shows instead as "Picks for both teams".
- `NoPick` = members (not M) with no pick on G.
- `HomePickers` / `AwayPickers` = members (not M) who picked that side. Always filled, whether or not M has a pick.
- `OppositeCount = OppositePicks.Count`.
- `SwingPoints = PointValue * OppositeCount` (informational).
- `MyOutcome`: `NoPick` when M has no pick; otherwise `Won` / `Lost` as soon as the game has a **determinable winner** (below), and `Pending` until then. A Final tie or a Final game with a score missing has no winner, so it reads `Pending` - the "needs review" path of section 7, not a loss.

M never appears in any of their own lists, and everybody else is listed in the order `MembersActiveAtLock` gave.

Ordering of `Games`: `OppositeCount desc`, then `PointValue desc`, then `KickoffUtc asc`, then `GameSetGameId asc` so two runs over the same inputs produce the same list (D-099). Games with `OppositeCount == 0` go to `EveryoneAgrees` (same secondary ordering) unless `MyOutcome == NoPick`, which stays in `Games`: a game M skipped has a zero count for want of a pick, not for want of disagreement, and still has both teams' pickers to show.

Header (D-097): `PointsSoFar` = sum of PointValue over games where `MyOutcome == Won`, which matches what section 7 will actually award M. `MaxRemaining` = sum of PointValue over games where M has a pick, the game is not Final, and it has no winner yet - the last clause only bites on a correction applied before a game went Final, and stops one game counting as both earned and still to come.

Winner determination is `Scoring/WinnerResolver` (D-098), shared with sections 7 and 8 and with `GameSetGameDtoMapper` / `LiveScoreApplyService`: `ResultOverrideWinnerTeamId` if set, else the higher score when `Status == Final`. Tie or missing scores when Final = no winner (flag for review, treat as Pending in the dashboard).

## 7. Scoring (Feature 06)

Owner: `Scoring/WeekScorer`. Tests: `WeekScorerTests`.

Trigger: `GameWentFinal`, `ResultOverridden`, `GameVoided`, and a nightly full recompute for safety.

`ScoreWeek(set, picks, memberships)` recomputes `WeekResults` for every membership that was active at lock (has a `WeekSubmissions` row) from scratch, then upserts. Because it is a full recompute from source rows, it is idempotent.

Per member: for each active game with a determinable winner - `Scoring/WinnerResolver.Resolve` / `HasDeterminableWinner`, the shared rule P6-01 built and section 6 also uses (D-098); never re-derive it here - `Points += PointValue` and `CorrectCount++` if pick == winner. Voided games contribute nothing and are excluded from `ActiveGameCount`. `IsWeekComplete` = every active game is Final with a winner (or voided). When a week first becomes Complete, write `SeasonStandingsSnapshots` for `ThroughWeek = week` (section 8) and emit nothing else.

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

Owner: `Infrastructure/Providers/GameMatcher` (matching) and `Infrastructure/Services/LiveScoreApplyService` (applying). Tests: `GameMatcherTests`, `TeamNameNormalizerTests`, `EspnScoreboardParserTests`, `LiveScoreApplyTests`.

- CFBD is the source of truth for `Games`. ESPN supplies status, scores, period, clock.
- **Candidate window.** One apply run is given an Eastern date and loads the games kicking off on that date *or the day before*. ESPN buckets its own payload by Eastern date, so a Saturday call already covers a 22:30 ET kickoff that goes final at 01:45 ET on Sunday; the extra day exists for the caller that asks on Sunday instead (a poller tick after midnight, a manual refresh, or the CFBD fallback, whose week query has no Eastern-day bucketing). The date is therefore *implicit in the candidate set*, not part of the match key - two adjacent days cannot make the pair ambiguous, because no team plays twice in two days.
- **Match order.** `EspnEventId` first (every poll after the first), then the normalized team pair, then the same pair with the sides swapped - ESPN labels one side "home" even at a neutral site and does not always agree with CFBD, and a swapped match must cross the scores over. Two candidate games for one pair is *ambiguous* and is never guessed. Once matched, store `EspnEventId` and both `EspnTeamId`s so the id path serves the next poll.
- **Normalization** = fold diacritics (`FormD` + strip non-spacing marks: ESPN ships `San José State` with U+00E9), lower case, drop punctuation entirely rather than turning it into a separator (so `Hawai'i` = `Hawaii`, while whitespace still separates and `Miami (OH)` never collides with `Miami`), collapse whitespace, drop the word "university". Compare ESPN **`team.location`** - the school name with no mascot - against `Teams.School`, then `TeamAliases(Source=Espn)`, then, only as a last resort and only on an exact case-insensitive hit, `Teams.Abbreviation`. An abbreviation two schools share resolves to *nothing*: ESPN's abbreviations are its own (`USA`, `USM`, `USF`, `TA&M`) and a wrong match silently scores the wrong game.
- **Unmatched vs ignored.** Classification decides what happens to an event we could *not* place; it never decides whether to place one, so an FCS opponent that is genuinely on our schedule still gets its live score. An unplaceable event is ignored silently when either side resolves to a known FCS-or-lower school, or when neither side resolves to any known school (`groups=80` is not an FBS filter - a Saturday payload is full of FCS-vs-FCS noise, D-012). Otherwise one `UnmatchedGames` row is written per `(Source, RawHomeName, RawAwayName, GameDate)`, with the raw payload, and surfaced on the data page.
- **The CFBD fallback carries no team names.** Its updates are identified by `Games.CfbdGameId` in the event-id field; a CFBD game we do not hold is ignored, never written to `UnmatchedGames`.
- **Status mapping** from ESPN `status.type.name`: `STATUS_SCHEDULED` -> Scheduled; `STATUS_IN_PROGRESS` / `STATUS_HALFTIME` / `STATUS_END_PERIOD` / `STATUS_END_OF_PERIOD` / `STATUS_DELAYED` / `STATUS_RAIN_DELAY` -> InProgress; `STATUS_FINAL` -> Final; `STATUS_POSTPONED` / `STATUS_SUSPENDED` -> Postponed; `STATUS_CANCELED` -> Cancelled. ESPN's name set is open, so an **unrecognized name is logged once and mapped from `status.type.state`** instead of crashing a Saturday (D-012): `pre` -> Scheduled, `in` -> InProgress, `post` -> Final when `type.completed`, and InProgress when not - the only non-terminal status, which can never fire `GameWentFinal` and is corrected by the next poll. Neither name nor state understood: the event is skipped.
- **Scores are never read while the game is Scheduled.** ESPN reports `"0"` for both sides before kickoff, and reading it would fabricate a tie. `Period` and `Clock` are written only while a game is InProgress and cleared otherwise.
- **Games move forwards.** A stale payload never walks a Final game back to InProgress, nor a running game back to Scheduled. Postponement and cancellation may happen at any point and may be undone.
- `GameWentFinal` fires exactly once per game, guarded on the previous status, and carries the *provider* week from the `Games` row, so a post-midnight finish still scores against the week it was scheduled in. Its `WinnerTeamId` is the higher score, and null on a tie or missing scores - the "needs review" path of section 7, not a win. `GameScheduleChanged` fires on the way into Postponed or Cancelled and on the way back out.
- **Applying the same snapshot twice changes no row and raises no event**, `LastScoreUpdateUtc` included; that is what makes the five-minute cadence, the catch-up window and a manual refresh all harmless.

## 10. Saturday poller cadence (Features 06, 09, 12)

Owner: `Infrastructure/Jobs/SaturdayPoller`. Tests: `SaturdayPollerScheduleTests`.

- Active window per Saturday (ET): from `min(LockAtUtc across leagues) - 5 min` until every active game in every active league's set is Final/Voided/Postponed/Cancelled, or 3:00 AM ET Sunday, whichever first.
- Cadence 5 minutes with ESPN. If `Providers__LiveScores = Cfbd`, cadence 10 minutes. Outside the window: no calls.
- On 3 consecutive ESPN failures, switch to CFBD fallback for the rest of the day and set `ScoresMayBeStale = true` on dashboards; log a warning. Reset next Saturday.
- That switch is already built (P2-03): `ILiveScoreHealth` (singleton) owns `ActiveSource` / `ConsecutiveFailures` / `ScoresMayBeStale` and `CompositeLiveScoreProvider` is the single `ILiveScoreProvider` the poller injects, so the poller never chooses a source. It calls `ResetForNewDay()` once when a game-day window opens, reads `ActiveSource` for its cadence, and hands what it fetched to `LiveScoreApplyService.ApplyAsync(easternDate, updates, ct)`.
- The fallback banner must say more than "stale": CFBD's games endpoint has no period, no clock, no live odds and no Postponed or Cancelled (D-012).
- Every call recorded in `ProviderCalls` by `IProviderCallRecorder`. Monthly CFBD count exposed; warn at 800.

## 11. Notification scheduling (Feature 11)

Owner: `Infrastructure/Jobs/ReminderJobs`. Tests: `ReminderJobTests`.

- Cron evaluated in Eastern: Friday 20:00 (member reminder), Friday 21:00 (commissioner summary), and a per-week one-shot at `LockAtUtc - 1h` (Saturday reminder) recomputed whenever `LockAtUtc` changes.
- Recipients evaluated at send time. Skip members with status Submitted. Skip everything if the league has no game set for the current week or the week is already locked.
- Once-per-week guarantee via the `NotificationLog` filtered unique index; a duplicate insert = Skipped.
- Event-driven: `GameAddedToSet` (only to members who were Submitted) and `GameRemovedFromSet` (only to members with a pick on that game) send immediately, coalescing multiple adds in one regeneration into one "N new games" message.
- Delivery: 404/410 deletes the subscription (Result = Expired). Other failures retry 3 times over 15 minutes with backoff, then Failed.
