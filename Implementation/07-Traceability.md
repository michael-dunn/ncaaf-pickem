# 07 - Traceability

Every acceptance-criteria group in the 13 stories, the task that delivers it, and the test or check that proves it. P8-04 walks this table and records a result per row in `STATUS.md`. "Manual" rows need a recorded manual verification with date and device.

Legend: **D** = Domain unit test, **A** = API integration test, **UI** = manual check at 375px (screenshot), **Ops** = manual operational check.

## Feature 01 - Leagues and Members

| AC group | Task | Proof |
|---|---|---|
| League creation (name, season, creator is commissioner + member, 50-char name) | P1-01 | A `LeagueEndpointsTests.Create*` |
| One season per league, no rollover | P1-01 | A: SeasonYear required; no rollover endpoint exists |
| 50-member cap and "league is full" | P1-01 | A `InviteAcceptTests.GivenFullLeague_*` |
| Invite generation shareable by text | P1-01, P1-02 | A `InviteTests`; UI share sheet |
| Valid invite joins and lands on home; revoked/expired message; no double join | P1-01, P1-02 | A `InviteAcceptTests` x4 |
| Mid-season join: 0 points, no earlier weeks | P1-01, P5-03 | A `StandingsTests.GivenLateJoiner_*` |
| Remove member keeps picks as former member | P1-01, P5-03 | A `MembershipTests.Remove*`, `WeekLeaderboardTests.FormerMember*` |
| Cannot remove self; transfer demotes only the transferer; promote/demote; at least one commissioner | P1-01 | A `RoleTests` x5 |
| League home shows name, week, my status, links | P1-02 | UI |
| Multi-league picker | P1-02 | UI + A `MeTests.ListsLeagues` |
| Mobile 375px | P1-02 | UI |

## Feature 02 - Weekly Game Set Configuration

| AC group | Task | Proof |
|---|---|---|
| Rules saved to default config | P3-03 | A `GameSetRulesEndpointsTests` |
| Union of rules, distinct | P3-01 | D `GameSetGeneratorTests.GivenSeveralRules_WhenGenerating_ThenTheResultIsTheirUnionWithEachGameOnce` |
| Top 25 uses current AP poll | P3-01 | D `*.GivenTwoPollsForTheWeek_WhenGenerating_ThenTheLatestFetchWinsAndNoFallbackIsFlagged`, `*.GivenNoPollForTheWeek_WhenGenerating_ThenThePriorWeekPollIsUsedAndFlagged` |
| Conference-games-only | P3-01 | D `*.GivenAConferenceRule_WhenConferenceGamesOnlyIsOff_ThenEitherTeamQualifies`, `*...IsOn_ThenBothTeamsMustQualify` |
| Team bye yields nothing | P3-01 | D `*.GivenATeamRule_WhenThatTeamHasAByeWeek_ThenNoGameIsAdded` |
| Saturday in Eastern only | P3-01, P0-05 | D `SeasonCalendarTests` (Friday Pacific is Saturday Eastern), `GameSetGeneratorTests.GivenFridayKickoffs_WhenGenerating_ThenOnlyTheFridayPacificGameIsSaturdayEastern` |
| FCS excluded | P3-01 | D `*.GivenAnFcsOpponent_WhenGenerating_ThenTheGameIsNeverIncluded`, `*.GivenAnFcsHomeTeam_...` |
| AP only | P3-03 | A: RuleType enum has no other poll |
| 50-game cap with preview warning | P3-01, P3-03, P3-05 | D `*.GivenFiftyMatchingGames_WhenGenerating_ThenTheCapIsNotExceeded`, `*.GivenMoreThanFiftyMatchingGames_...`, A 409, UI warning |
| Cancelled/postponed excluded and removed | P3-01, P3-04 | D `*.GivenPostponedAndCancelledGames_WhenGenerating_ThenNeitherIsIncluded`, `*.GivenAManualGameThatWasCancelled_WhenRegenerating_ThenItLeavesTheSetAsIneligible`, A `RegenerationJobTests.GivenAPostponedGame_WhenTheTuesdayJobRuns_ThenItIsRemovedAsAScheduleChange` |
| Week override leaves default intact | P3-03 | A |
| Manual remove sticky across regen; manual add included | P3-01 | D `*.GivenAManuallyRemovedGame_WhenRegenerating_ThenItStaysOutOfTheSet`, `*.GivenAManuallyAddedGame_WhenRegenerating_ThenItIsKeptThoughNoRuleMatchesIt`, `*.GivenNarrowedRules_WhenRegenerating_ThenOnlyRuleRowsAreRemovedAndManualRowsSurvive` |
| Preview lists matchups with ranks and count | P3-01, P3-03, P3-05 | D `*.GivenCandidateRules_WhenPreviewing_ThenManualAddsAndStickyRemovalsStillApply`, A, UI |
| Generated on save and Tuesday auto-regen; frozen after lock | P3-04 | A `RegenerationJobTests`, `AutoCreateWeekSetTests`, `WeekOverrideRegenerateTests`, D `GameSetGeneratorTests.GivenALockedWeek_WhenGenerating_ThenNothingIsProducedAndTheRefusalIsFlagged` |
| Member view ordered by kickoff in local time | P3-05 | UI |
| Tap-only rule editing | P3-05 | UI |

## Feature 03 - Point Values

| AC group | Task | Proof |
|---|---|---|
| Default 10; change 1..100 applies to unmatched games | P3-02, P3-03 | D, A |
| No rule = default; one rule = its value; multiple = highest priority | P3-02 | D `PointValueResolverTests` x3 |
| Close spread with no spread = no match | P3-02 | D |
| Daily spread refresh, snapshot at lock | P2-04, P4-02 | A `LockWeekJobTests.GivenADueWeek_WhenTheJobRuns_ThenEachActiveGameHasItsSpreadAndPointValueFrozen`, D `WeekLockerTests` |
| Commissioner-only weighting | P3-03 | A: no member endpoint exists |
| Override wins; per-week only | P3-02, P3-03 | D, A |
| Point value visible and elevated badge | P4-03 | UI |
| Frozen after lock | P3-03, P4-02 | A `PostLockMutationTests` (409 for generate/add/remove/week-rules/override, after the job and in the pre-job window), A `LockWeekJobTests.GivenALockedWeek_WhenAPointRuleAndTheLineChange_ThenTheFrozenValuesDoNotMove` |
| Mid-week change updates submitted members' view, picks valid | P3-03 | A `PointRulesChange_KeepsPicks` |

## Feature 04 - Weekly Picks

| AC group | Task | Proof |
|---|---|---|
| Picks page lists teams, rank, kickoff, points | P4-03 | UI |
| Tap picks, other unmarked, re-tap is no-op | P4-01, P4-03 | A `SetPickTests`, UI |
| Auto-save with indicator; failure reverts | P4-03 | UI (simulate offline) |
| Submit only when all picked; remaining count shown | P4-01, P4-03 | A `SubmitTests`, UI |
| Change after submit keeps Submitted | P4-01 | D `SubmissionStatusTests` |
| Past weeks read-only | P4-01 | A 409 on old week |
| Game added reverts to In Progress, highlighted, notified | P4-04, P7-03 | A `GameAddedTests`, UI, A `NotificationTests.GamesAdded` |
| Game removed keeps its pick row, counts update, member flagged only if they had a pick on it | P4-04 | A `GameRemovedTests` |
| Server-side lock enforcement | P4-01, P4-02 | A `LockEnforcementTests` |
| Unpicked at lock = Incomplete and 0 points | P4-02, P5-01 | D `WeekLockerTests`, D `WeekScorerTests.NoPickScoresZero` |
| Server-side lock enforcement | P4-01, P4-02 | A `LockEnforcementTests`, A `PostLockMutationTests` |
| Unpicked at lock = Incomplete and 0 points | P4-02, P5-01 | D `WeekLockerTests`, A `LockWeekJobTests.GivenASubmitterAndAPartialPicker_WhenTheJobRuns_ThenOneIsLockedAndTheOtherIncomplete`, D `WeekScorerTests.NoPickScoresZero` |
| Picks hidden before lock, visible after | P4-01 | A `PicksVisibilityTests` |
| Status values on home; commissioner roster | P4-01, P1-02 | A, UI |
| 44px targets, sticky submit, 2 s interactive | P4-03, P0-04 | UI, spike measurement |

## Feature 05 - Pick Lock and Influence Dashboard

| AC group | Task | Proof |
|---|---|---|
| Lock = earliest Saturday kickoff | P3-01, P0-05 | D `LockAtIsEarliestKickoff` |
| Week locks at that instant: statuses settled, values frozen, `WeekLocked` raised | P4-02 | D `WeekLockerTests`, A `LockWeekJobTests` (due/not due, catch-up, idempotent second run, late joiner, event raised once, scheduler registration) |
| Pre-lock countdown; post-lock available | P6-02, P6-03 | A `DashboardTests.GivenAWeekWithNoGameSet_WhenAMemberAsksForTheDashboard_ThenItIsUnavailableWithNoLockTime`, `.GivenAGeneratedSetPastItsLockInstant_WhenTheJobHasNotRun_ThenTheDashboardIsStillUnavailable`, `.GivenTheOverviewExample_WhenDanceAsksForHerDashboard_ThenItMatchesTheWorkedExample` (post-lock available); UI |
| Opposite picks definition; No Pick group; viewer excluded; viewer no-pick case | P6-01 | D `InfluenceCalculatorTests.GivenTheWorkedExample_*` (the Overview example, from `influence-example.json`), `.GivenTheViewerPicked_WhenBuildingTheDashboard_ThenTheyAreInNoneOfTheirOwnLists`, `.GivenAMemberWithNoPick_*`, `.GivenTheViewerDidNotPick_WhenBuildingTheDashboard_ThenItStaysInGamesWithBothTeamsLists` |
| Only members active at lock are listed (former in, post-lock joiner out); voided games excluded | P6-01 | D `InfluenceCalculatorTests.GivenAFormerMemberWhoWasActiveAtLock_*`, `.GivenAMemberWhoJoinedAfterLock_*`, `.GivenAVoidedGame_*` |
| Ordering and tie-breaks; Everyone-agrees collapsed | P6-01, P6-03 | D `InfluenceCalculatorTests.GivenGamesWithDifferentOpposition_*`, `.GivenTwoGamesAlikeInEveryOrderingKey_*`, `.GivenAGameEverybodyAgreesOn_*`; UI |
| Game status display; Won/Lost marking | P6-01, P6-03 | D `InfluenceCalculatorTests.GivenAFinalScore_*`, `.GivenAResultOverride_*`, `.GivenAFinalTie_*`, `.GivenAGameInProgress_*`, `WinnerResolverTests`; UI |
| Points so far and max remaining | P6-01 | D `InfluenceCalculatorTests.GivenAWeekPartlyPlayed_WhenBuildingTheHeader_ThenPointsSoFarAndMaxRemainingAreSummed` |
| Card layout, names wrap, live refresh without reload | P6-03 | UI |
| No view-as-other, no league-wide split | P6-02 | A `DashboardTests.GivenAnExtraQueryParameter_WhenAMemberAsksForTheDashboard_ThenItIs400`, `.GivenAMemberWhoJoinedAfterLock_WhenTheyAskForTheirOwnDashboard_ThenTheySeeNoPickEverywhere`, `.GivenAFormerMemberWhoPickedBeforeLeaving_WhenTheWeekLocks_ThenTheyStillAppearFlagged`, `.GivenAStaleLiveScoreSource_*`, `.GivenAFinalGameWithAWinner_*`, `.GivenTheAuthMatrix_WhenAskingForTheDashboard_ThenOnlyAMemberMaySeeIt` |

## Feature 06 - Scoring

| AC group | Task | Proof |
|---|---|---|
| Correct pick earns locked value; wrong/none earns 0 | P5-01 | D `WeekScorerTests.GivenAMemberPickedTheWinner...`, `...PickedTheLoser...`, `...DidNotPick...` |
| Idempotent; not-final unscored | P5-01 | D `WeekScorerTests.GivenTheSameWeek_WhenScoredTwice...`, `...GivenAGameThatIsNotFinal...`; A `ScoringSnapshotWalkTests` (two extra rescores change nothing) |
| Weekly total; Complete flag | P5-01 | D `WeekScorerTests.GivenSeveralGames_WhenScoring_ThenTheWeeklyTotalIsTheSum...`, `...GivenEveryActiveGameFinalWithAWinner...`, `...GivenOneGameStillToPlay...` |
| Post-midnight delayed game counts | P5-01, P2-03 | D `WeekScorerTests.GivenTheFixturesPostMidnightFinish...` (read out of snapshot 6); A `ScoringSnapshotWalkTests` (snapshot 6 adds the late game's points to week 7) |
| Nightly recompute keeps results true | P5-01 | A `ScoringServiceTests.GivenALockedWeekNobodyScored...`, `...GivenASetWhoseResultsDisagreeWithIt...` |
| No tiebreakers | P5-03 | D `StandingsCalculatorTests.TiesShareRank` |
| Tie/no winner flagged, 0 to all | P5-01, P2-04 | D `WeekScorerTests.GivenAFinalTie...`, `...GivenAFinalGameMissingAScore...`; A `ScoringSnapshotWalkTests` (tie on the data-status needs-review list, week stays open) |
| Void removes from scoring, shows Voided | P5-01, P5-02, P5-04 | D `WeekScorerTests.GivenAVoidedGame...` (x2); A `ScoringServiceTests.GivenAGameIsVoided...`, `...GivenTheTieIsVoided...`; A `VoidTests`, UI grid |
| Override recalculates; audit logged and visible | P5-01, P5-02, P5-05 | D `WeekScorerTests.GivenATieAnOverrideHasSettled...`; A `ScoringSnapshotWalkTests` (real `ResultOverridden` closes the week); A `OverrideTests`, UI audit |
| 5-minute checks; score only on Final | P2-04, P2-03 | D `SaturdayPollerScheduleTests`, D `GameMatcherTests.FinalOnlyOnFinal` |

## Feature 07 - Leaderboard

| AC group | Task | Proof |
|---|---|---|
| Season rows, competition ranking, behind leader, weekly wins, highlight | P5-03, P5-04 | D `StandingsCalculatorTests`, A `LeaderboardEndpointsTests.GivenScoredWeeks_...`, UI |
| Trend indicator; none on first week | P5-03 | D `StandingsCalculatorTests.GivenTwoSnapshotWeeks_...` / `GivenOnlyOneSnapshotWeek_...`, A `LeaderboardEndpointsTests.GivenTwoCompletedWeeks_...` |
| No champion banner | P5-04 | UI |
| Week rows, correct count, trophy, ties share | P5-03, P5-04 | D `StandingsCalculatorTests.GivenAWeekThatIsNotComplete_...`, A `LeaderboardEndpointsTests.GivenACompletedWeek_...`, UI |
| In Progress label | P5-04 | UI (server flag: A `LeaderboardEndpointsTests.GivenAWeekStillBeingPlayed_...`) |
| Grid with colors, voided greyed, pinned column, horizontal scroll | P5-03, P5-04 | D `StandingsCalculatorTests.GivenAGrid_...`, A `LeaderboardEndpointsTests.GivenALockedWeek_...`, UI |
| Grid hidden before lock | P5-03 | A `LeaderboardEndpointsTests.GivenAnUnlockedWeek_WhenReadingTheGrid_ThenItIs403` |
| Prev/next, jump to current, only generated weeks | P5-03, P5-04 | UI, A `LeaderboardEndpointsTests.GivenAWeekWhoseSetHasNoGames_WhenListingLeagueWeeks_ThenItIsNotNavigable` |
| Former members in past weeks only | P5-03 | D `StandingsCalculatorTests.GivenAFormerMember_...`, A `LeaderboardEndpointsTests.GivenACompletedWeek_...` |
| Mid-season joiners scored from their own weeks | P5-03 | D `StandingsCalculatorTests.GivenAMidSeasonJoiner_...`, A `LeaderboardEndpointsTests.GivenScoredWeeks_...` |
| Under 1 s for 50 members x 15 weeks | P5-03 | A `LeaderboardPerfTests` with seeded data |
| Leaderboard authorization | P5-03 | A `LeaderboardAuthMatrixTests` |

## Feature 08 - Authentication

| AC group | Task | Proof |
|---|---|---|
| First login creates account; returning matched by subject | P0-03 | A `AuthTests` with fake Google handler |
| Works in iOS standalone | P0-04, P8-03 | Manual on iPhone |
| 90-day sliding cookie, HttpOnly Secure; logout invalidates | P0-03 | A cookie attribute assertions |
| Display name 1..30 everywhere; unique per league | P0-03, P1-03 | A |
| Member/commissioner authorization; non-member 404 | P0-03 + every endpoint task | A auth matrix test per group |

## Feature 09 - Game Data Feed

| AC group | Task | Proof |
|---|---|---|
| Provider abstraction; local storage only | P2-02, P2-03 | Architecture review; A: features read DbContext only |
| Refresh cadences | P2-04 | A `RefreshJobScheduleTests` (incl. the 2026-11-01 DST week) |
| Idempotent refreshes | P2-02 | A `RefreshTwice_NoDuplicates` |
| Failure keeps old data, logs; stale banner | P2-02, P2-04, P6-02 | A `ReferenceIngestTests`; UI `DataStatusPage`'s `ScoresMayBeStale` banner |
| Rate limits respected | P2-04 | D `SaturdayPollerScheduleTests` (window/cadence/fallback); A `AdminEndpointsTests` (CFBD counter warning at 800) |
| Data status page | P2-04 | UI `Pages/Admin/DataStatusPage.razor`, `Implementation/screenshots/p2-04-data-status-375.png` |
| Saturday poller applies fixture snapshots, scores every game | P2-04 | A `SaturdayPollerIntegrationTests` (Phase 2 exit criterion) |

## Feature 10 - Hosting and Platform

| AC group | Task | Proof |
|---|---|---|
| Manifest, standalone, icons | P0-04 | Manual install on iPhone |
| 375px first; no horizontal scroll; 44px; 16px | all UI tasks | UI checklist in each card |
| Single deployable; env config; no secrets | P0-01, P8-02 | Ops |
| Jobs in-process | P0-06 | Architecture |
| Single DB; nightly backups 30 days; UTC storage | P8-02, P0-02 | Ops, D |
| No Saturday deploys | P8-02 | deploy script guard |

## Feature 11 - Notifications

| AC group | Task | Proof |
|---|---|---|
| Opt in stores subscription; opt out deletes; iOS install guidance; per-member | P7-01, P7-02 | A `PushSubscriptionTests`, UI |
| In-process scheduler; once per week; no set = none; status at send time; Saturday recomputed on lock move | P7-03 | A `ReminderJobTests` (recipients by status, 7:59 submit, no set, locked week, twice-in-a-week skip, real-cron test), `SaturdayOneShotTests` (due at `LockAtUtc-1h`, not due before, submitted-by-then, locked week, lock move -> new occurrence) |
| VAPID delivery; 404/410 cleanup; retries; log | P7-01 | A with fake push transport |
| Text excludes others' picks; opens standalone | P7-02 | Review, manual |
| Catalog #1/#3 (member reminders) and #2 (commissioner summary, only when someone unsubmitted, names listed) | P7-03 | A `ReminderJobTests` |
| Catalog #4 (games added, coalesced, previously-Submitted members only) and #5 (game removed, members with a pick only) | P7-03 | A `EventNotificationTests` |

## Feature 12 - Data Provider Evaluation

| AC group | Task | Proof |
|---|---|---|
| Separate reference and live interfaces | P2-02, P2-03 | Architecture |
| CFBD via official client and config key | P2-02 | A with recorded fixture |
| ESPN default, CFBD fallback, config switch | P2-03, P2-04 | A `LiveScoreSourceSwitchTests`; `SaturdayPollerScheduleTests` (cadence follows `ILiveScoreHealth.ActiveSource`) |
| Monthly counter, warning at 800 | P2-04 | A `AdminEndpointsTests` (799 -> no warning, 800 -> warning) |
| Name matching, unmatched surfaced | P2-03 | D `GameMatcherTests`, UI |
| Follow-ups: tier confirm, sample payload, alias table | P2-01 | Doc in `Implementation/spikes/` |

## Feature 13 - Season Calendar

| AC group | Task | Proof |
|---|---|---|
| Current week window; Sunday rollover ends picking | P0-05, P4-01 | D `SeasonCalendarTests`, A |
| Jump to current | P5-04 | UI |
| Season range defaults, Week 0 opt-in, regular season only | P0-05, P1-01 | D, A |
| Before first week / after last week states | P1-02 | UI, A |
| UTC storage, Eastern logic, local display with zone hint | P0-05, all UI | D, UI |
| Schedule change before lock removes + notifies; after lock flags for void review (`GameNeedsVoidReview`), P5-02 owns the actual void | P3-04, P7-03, P5-02 | A `ScheduleChangeTests` |
