# Phase 2 - Data Feed

Stories: 09 Game Data Feed, 12 Data Provider Evaluation.
Depends on: Phase 0 complete. Runs in parallel with Phases 1 and 3a. **P2-05 (fixtures) is on the critical path for every later UI and endpoint task; start it first.**

Read first: WorkItems 09, 12, `02-Data-Model.md` (Season reference data, Operations), `04-Domain-Algorithms.md` sections 9 and 10, `01-Architecture.md` provider isolation.

---

## P2-05 Fixtures and fixture providers (start first)
Tier: Sonnet. Depends on: P0-02.

Deliverables
- `tests/NcaafPickEm.Fixtures/Week7_2026/`: `teams.json` (all FBS + a handful of FCS), `conferences.json`, `schedule.json` (per `05-Conventions.md` testing section: 12 Saturday FBS, 2 FCS, 1 Friday, 1 Friday-Pacific-Saturday-Eastern, 2 conference games, 4 ranked teams), `rankings.json`, `lines.json` (10 of 12), `scores/snapshot-1.json` to `snapshot-6.json` (kickoff through all Final, one Final after midnight ET, one game with a tie score to exercise "needs review").
- `influence-example.json`: the Feature 05 worked example (5 members, 2 games) as a fixture for `InfluenceCalculatorTests`.
- `FixtureReferenceDataProvider`, `FixtureLiveScoreProvider` (snapshot index advanced by a test hook or a query string in Development), `FixtureSeasonWeekSource`.
- Development seeding: on startup with `Providers__ReferenceData=Fixture`, load teams/schedule/rankings/lines for 2026 week 7 into the DB if empty, and create a demo league with 5 members named after the worked example when `Seed__DemoLeague=true`.

Done when
- App runs offline in Development with a demo league and a full week of games.
- `FixtureLoaderTests` validate shapes.

## P2-01 Provider spike (real APIs)
Tier: Opus. Depends on: P0-01. Needs CFBD API key from the orchestrator.

Deliverables in `Implementation/spikes/providers.md`
- Confirm from the CFBD tiers page (and a test call) which tier serves the live scoreboard endpoint; record the answer and update Feature 12's follow-up.
- Capture one real ESPN scoreboard payload for a Saturday (`groups=80`, single `dates=YYYYMMDD`) and one rankings payload into `tests/NcaafPickEm.Fixtures/Real/` (trimmed). Document the fields used: `events[].id`, `events[].date`, `competitions[].status.type.name/completed`, `competitors[].homeAway/score/team.displayName/team.abbreviation/curatedRank.current`, `competitions[].odds[0].details/spread`, `groups`.
- Capture one CFBD response each for teams (fbs), games (year, week, division fbs), rankings, lines, and the calendar endpoint; record request counts consumed.
- Draft `TeamAliases` seed for name mismatches found between the two payloads.

Done when
- Spike doc exists with field mappings, sample files committed, and a DECISIONS entry if anything in Feature 12 or `04` section 9 needs changing.

## P2-02 CFBD reference data provider and ingest
Tier: Sonnet (Opus review). Depends on: P0-02, P2-01 field mappings.

Deliverables
- `CfbdReferenceDataProvider : IReferenceDataProvider` using the official NuGet client; every call wrapped to write `ProviderCalls`.
- `ReferenceDataIngestService`: upsert `Conferences`, `Teams` (Classification), `SeasonWeeks` (from calendar; `IsRegularSeason` from season type), `Games` (compute `KickoffEasternDate`, `IsSaturdayEastern`, `IsConferenceGame`), `Rankings` (AP only), `GameLines` (append with `FetchedUtc`). Upserts keyed by provider IDs.
- `DataRefreshStatus` updates on success/failure; failures keep prior data.

Done when
- `ReferenceIngestTests` using the fixture payloads: ingest twice yields no duplicates; postponed game status updates; FCS classification stored; Saturday-Eastern computed correctly for the Friday-Pacific game.

## P2-03 ESPN live score provider, matcher, CFBD fallback
Tier: Opus. Depends on: P2-02, P2-01.

Deliverables
- `EspnLiveScoreProvider : ILiveScoreProvider` (`HttpClient`, single-date query, `groups=80`), `CfbdLiveScoreProvider` (games endpoint for the week).
- `GameMatcher` per `04` section 9 with normalization and `TeamAliases`; writes `EspnEventId`/`EspnTeamId` on first match; writes `UnmatchedGames` once per unmatched pair.
- `LiveScoreApplyService`: apply status/score/period/clock to `Games`; detect transition to Final and raise `GameWentFinal` exactly once; detect Postponed/Cancelled transitions and raise `GameScheduleChanged`.
- Config switch `Providers__LiveScores` selects the implementation; runtime fallback flag when ESPN fails 3 times in a row.

Done when
- `GameMatcherTests` (aliases, abbreviations, unmatched surfaced), `LiveScoreApplyTests` (Final fires once; tie score does not produce a winner; post-midnight Final still applies to week 7), `LiveScoreSourceSwitchTests`.

## P2-04 Refresh jobs, Saturday poller, data status page
Tier: Sonnet (Opus review on the poller). Depends on: P0-06, P2-02, P2-03.

Deliverables
- Cron jobs (Eastern): `TeamsRefreshJob` weekly Tuesday 03:00; `ScheduleRefreshJob` Tuesday 03:10 and daily 04:00 Wed to Sat for status changes; `RankingsRefreshJob` Sunday 20:00 and Monday 20:00 and Tuesday 03:20; `LinesRefreshJob` daily 23:30.
- `SaturdayPoller : BackgroundService` per `04` section 10 (window, cadence, fallback, call accounting).
- `POST /api/admin/refresh/{dataType}` (audit logged) and full `GET /api/admin/data-status` including `CfbdCallsThisMonth`, warning at 800, unmatched list, "needs review" games (Final with no winner), recent job runs.
- Blazor **Data status** page (`/admin/data`) for commissioners: cards per data type with last success/error, refresh buttons, CFBD counter with warning color, unmatched games with a resolve picker, needs-review list linking to override/void (Phase 5 fills the actions).

Done when
- `RefreshJobScheduleTests` (cron strings resolve to expected Eastern times, including DST week), `SaturdayPollerScheduleTests` (window start/end, cadence by source, fallback after 3 failures), `AdminEndpointsTests` (commish-only, counter math).

---

## Phase exit criteria
- App runs offline on fixtures with a demo league (P2-05).
- Real CFBD ingest verified once with the orchestrator's key; call count recorded.
- Poller applies fixture snapshots 1 to 6 and raises `GameWentFinal` for each game once.
- Data status page shows refresh state and counter.
