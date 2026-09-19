# 07 - Traceability

Every acceptance-criteria group in the 13 stories, the task that delivers it, and the test or check that proves it. P8-04 walks this table and records a result per row in `STATUS.md`. "Manual" rows need a recorded manual verification with date and device.

Legend: **D** = Domain unit test, **A** = API integration test, **UI** = manual check at 375px (screenshot), **Ops** = manual operational check.

**Result column values**: `PASS: <TestClass>.<Method>` (grep-verified to exist in `tests/`, and part of the
green `dotnet test` run — 306 Domain.Tests + 516 Api.Tests, 822/822, this pass); `PASS: screenshot <file>`
for a UI row proven only by a captured 375px screenshot under `Implementation/screenshots/`;
`MANUAL PENDING (operator): <where the steps are>` for anything that needs a physical device, a live
OAuth round trip, or the deployed home server; `GAP -> follow-up` for anything neither proven nor
verifiable in this environment, with the follow-up recorded in `STATUS.md`'s Escalations table. Where
the Proof column's cited class/method name was stale (renamed or never existed under that name), the
Result gives the real one and the Proof column is left as originally written for history.

## Feature 01 - Leagues and Members

| AC group | Task | Proof | Result |
|---|---|---|---|
| League creation (name, season, creator is commissioner + member, 50-char name) | P1-01 | A `LeagueEndpointsTests.Create*` | PASS: `LeagueEndpointsTests.GivenALoggedInUser_WhenCreatingALeague_ThenDefaultsAreAppliedAndCreatorIsCommissioner`, `.GivenAnEmptyName_WhenCreatingALeague_ThenItIsRejected`, `.GivenAMissingName_WhenCreatingALeague_ThenItIsRejected` |
| One season per league, no rollover | P1-01 | A: SeasonYear required; no rollover endpoint exists | PASS: `LeagueEndpointsTests.GivenAChampionshipWeekAsLastWeek_WhenCreatingALeague_ThenItIsRejected` (season year required); route inventory (`RouteInventoryTests`) confirms no rollover endpoint is mapped |
| 50-member cap and "league is full" | P1-01 | A `InviteAcceptTests.GivenFullLeague_*` | PASS: `InviteAcceptTests.GivenALeagueAtTheMemberCap_WhenAcceptAttempted_ThenItIs409WithFullState` (real method name — the Proof column's `GivenFullLeague_*` was never the actual name) |
| Invite generation shareable by text | P1-01, P1-02 | A `InviteTests`; A `FullWeekSimulationTests` (one code, five members accept it); UI share sheet | PASS: `AuthMatrixTests.GivenTheInvitesGroup_WhenCalledByEachRole_ThenTheMatrixHolds` (creation succeeds for a commissioner), `FullWeekSimulationTests` (one invite code, five members accept it); PASS: screenshot `p1-02-invites-375.png` (share sheet). No `InviteTests` class exists — it was never real |
| Valid invite joins and lands on home; revoked/expired message; no double join | P1-01, P1-02 | A `InviteAcceptTests` x4 | PASS: `InviteAcceptTests.GivenAValidInvite_WhenAccepted_ThenTheCallerJoinsAtTheCurrentWeekAndUsesIncrement`, `.GivenAnExpiredInvite_WhenAcceptAttempted_ThenItIs409WithExpiredState`, `.GivenARevokedInvite_WhenAcceptAttempted_ThenItIs409WithRevokedState`, `.GivenACallerAlreadyAMember_WhenAcceptAttempted_ThenItIs409WithAlreadyMemberState` |
| Mid-season join: 0 points, no earlier weeks | P1-01, P5-03 | A `StandingsTests.GivenLateJoiner_*` | PASS: `StandingsCalculatorTests.GivenAMidSeasonJoiner_WhenTotallingTheSeason_ThenOnlyTheirOwnWeeksCount` (real class — `StandingsTests` was never real) |
| Remove member keeps picks as former member | P1-01, P5-03 | A `MembershipTests.Remove*`, `WeekLeaderboardTests.FormerMember*` | PASS: `RoleTests.GivenACommissioner_WhenRemovingAMember_ThenTheyAreSoftDeletedAndAuditLogged`, `.GivenARemovedMember_WhenListingMembers_ThenTheyAreFlaggedFormer`, `StandingsCalculatorTests.GivenAFormerMember_WhenBuildingBothLeaderboards_ThenTheyAreOnTheWeekButNotTheSeason`, `LeaderboardEndpointsTests.GivenACompletedWeek_WhenReadingItsLeaderboard_ThenFormerMembersAppearAndTheWinnersAreMarked` (real classes — `MembershipTests`/`WeekLeaderboardTests` were never real) |
| Cannot remove self; transfer demotes only the transferer; promote/demote; at least one commissioner | P1-01 | A `RoleTests` x5 | PASS: `RoleTests.GivenTheOnlyCommissioner_WhenRemovingThemself_ThenItIs409AsTheLastCommissioner`, `.GivenTheOnlyCommissioner_WhenDemotingThemself_ThenItIs409`, `.GivenATransfer_WhenApplied_ThenTargetIsPromotedCallerIsDemotedAndOthersAreUntouched`, `.GivenACommissioner_WhenPromotingAMember_ThenTheyBecomeCommissioner`, `.GivenTwoCommissioners_WhenDemotingOne_ThenTheOtherRemainsCommissioner` |
| League home shows name, week, my status, links | P1-02 | UI | PASS: screenshot `p1-02-league-home-commish-375.png`, `p1-02-league-home-member-375.png` |
| Multi-league picker | P1-02 | UI + A `MeTests.ListsLeagues` | PASS: `MeEndpointTests.GivenAMemberOfALeague_WhenGettingMe_ThenLeaguesIsFilled` (real class — `MeTests` was never real); screenshot `p1-02-picker-with-leagues-375.png` |
| Mobile 375px | P1-02 | UI | PASS: screenshots `p1-02-*-375.png` (all 9 states, `scrollWidth === 375` per the P1-02 STATUS note) |

## Feature 02 - Weekly Game Set Configuration

| AC group | Task | Proof | Result |
|---|---|---|---|
| Rules saved to default config | P3-03 | A `GameSetRulesEndpointsTests` | PASS: `GameSetRulesEndpointsTests.GivenValidRules_WhenPuttingDefaultRules_ThenTheyRoundTripWithEchoedNames`, `.GivenNoRulesSaved_WhenGettingDefaultRules_ThenTheArrayIsEmpty` |
| Union of rules, distinct | P3-01 | D `GameSetGeneratorTests.GivenSeveralRules_WhenGenerating_ThenTheResultIsTheirUnionWithEachGameOnce` | PASS: `GameSetGeneratorTests.GivenSeveralRules_WhenGenerating_ThenTheResultIsTheirUnionWithEachGameOnce` |
| Top 25 uses current AP poll | P3-01 | D `*.GivenTwoPollsForTheWeek_WhenGenerating_ThenTheLatestFetchWinsAndNoFallbackIsFlagged`, `*.GivenNoPollForTheWeek_WhenGenerating_ThenThePriorWeekPollIsUsedAndFlagged` | PASS: `GameSetGeneratorTests.GivenTwoPollsForTheWeek_WhenGenerating_ThenTheLatestFetchWinsAndNoFallbackIsFlagged`, `.GivenNoPollForTheWeek_WhenGenerating_ThenThePriorWeekPollIsUsedAndFlagged` |
| Conference-games-only | P3-01 | D `*.GivenAConferenceRule_WhenConferenceGamesOnlyIsOff_ThenEitherTeamQualifies`, `*...IsOn_ThenBothTeamsMustQualify` | PASS: `GameSetGeneratorTests.GivenAConferenceRule_WhenConferenceGamesOnlyIsOff_ThenEitherTeamQualifies` (and its `...IsOn_...` pair) |
| Team bye yields nothing | P3-01 | D `*.GivenATeamRule_WhenThatTeamHasAByeWeek_ThenNoGameIsAdded` | PASS: `GameSetGeneratorTests.GivenATeamRule_WhenThatTeamHasAByeWeek_ThenNoGameIsAdded` |
| Saturday in Eastern only | P3-01, P0-05 | D `SeasonCalendarTests` (Friday Pacific is Saturday Eastern), `GameSetGeneratorTests.GivenFridayKickoffs_WhenGenerating_ThenOnlyTheFridayPacificGameIsSaturdayEastern` | PASS: `SeasonCalendarTests`, `GameSetGeneratorTests.GivenFridayKickoffs_WhenGenerating_ThenOnlyTheFridayPacificGameIsSaturdayEastern` |
| FCS excluded | P3-01 | D `*.GivenAnFcsOpponent_WhenGenerating_ThenTheGameIsNeverIncluded`, `*.GivenAnFcsHomeTeam_...` | PASS: `GameSetGeneratorTests.GivenAnFcsOpponent_WhenGenerating_ThenTheGameIsNeverIncluded`, `.GivenAnFcsHomeTeam_...` |
| AP only | P3-03 | A: RuleType enum has no other poll | PASS: `RuleType` enum has no other poll value (checked in `Shared/Enums`) |
| 50-game cap with preview warning | P3-01, P3-03, P3-05 | D `*.GivenFiftyMatchingGames_WhenGenerating_ThenTheCapIsNotExceeded`, `*.GivenMoreThanFiftyMatchingGames_...`, A 409, UI warning | PASS: `GameSetGeneratorTests.GivenFiftyMatchingGames_WhenGenerating_ThenTheCapIsNotExceeded`, `.GivenMoreThanFiftyMatchingGames_...`; screenshot `p3-05-rules-preview-375.png` |
| Cancelled/postponed excluded and removed | P3-01, P3-04 | D `*.GivenPostponedAndCancelledGames_WhenGenerating_ThenNeitherIsIncluded`, `*.GivenAManualGameThatWasCancelled_WhenRegenerating_ThenItLeavesTheSetAsIneligible`, A `RegenerationJobTests.GivenAPostponedGame_WhenTheTuesdayJobRuns_ThenItIsRemovedAsAScheduleChange` | PASS: `GameSetGeneratorTests.GivenPostponedAndCancelledGames_WhenGenerating_ThenNeitherIsIncluded`, `.GivenAManualGameThatWasCancelled_WhenRegenerating_ThenItLeavesTheSetAsIneligible`, `RegenerationJobTests.GivenAPostponedGame_WhenTheTuesdayJobRuns_ThenItIsRemovedAsAScheduleChange` |
| Week override leaves default intact | P3-03 | A | PASS: `GameSetRulesEndpointsTests.GivenAnOverrideThenCleared_WhenGettingWeekRules_ThenItEchoesTheDefaultAgain` |
| Manual remove sticky across regen; manual add included | P3-01 | D `*.GivenAManuallyRemovedGame_WhenRegenerating_ThenItStaysOutOfTheSet`, `*.GivenAManuallyAddedGame_WhenRegenerating_ThenItIsKeptThoughNoRuleMatchesIt`, `*.GivenNarrowedRules_WhenRegenerating_ThenOnlyRuleRowsAreRemovedAndManualRowsSurvive` | PASS: `GameSetGeneratorTests.GivenAManuallyRemovedGame_WhenRegenerating_ThenItStaysOutOfTheSet`, `.GivenAManuallyAddedGame_WhenRegenerating_ThenItIsKeptThoughNoRuleMatchesIt`, `.GivenNarrowedRules_WhenRegenerating_ThenOnlyRuleRowsAreRemovedAndManualRowsSurvive` |
| Preview lists matchups with ranks and count | P3-01, P3-03, P3-05 | D `*.GivenCandidateRules_WhenPreviewing_ThenManualAddsAndStickyRemovalsStillApply`, A, UI | PASS: `GameSetGeneratorTests.GivenCandidateRules_WhenPreviewing_ThenManualAddsAndStickyRemovalsStillApply`; screenshot `p3-05-rules-preview-375.png` |
| Generated on save and Tuesday auto-regen; frozen after lock | P3-04 | A `FullWeekSimulationTests` (the real Tuesday 03:30 ET cron builds the week through `SchedulerTick`), A `RegenerationJobTests`, `AutoCreateWeekSetTests`, `WeekOverrideRegenerateTests`, D `GameSetGeneratorTests.GivenALockedWeek_WhenGenerating_ThenNothingIsProducedAndTheRefusalIsFlagged` | PASS: `FullWeekSimulationTests`, `RegenerationJobTests`, `AutoCreateWeekSetTests`, `WeekOverrideRegenerateTests.GivenTheCurrentWeek_WhenAnOverrideIsSaved_ThenTheSetIsRegeneratedInTheSameCall`, `GameSetGeneratorTests.GivenALockedWeek_WhenGenerating_ThenNothingIsProducedAndTheRefusalIsFlagged` |
| Member view ordered by kickoff in local time | P3-05 | UI | PASS: screenshot `p3-05-member-week-375.png` |
| Tap-only rule editing | P3-05 | UI | PASS: screenshot `p3-05-rules-375.png` (`BottomSheet`/`NumberStepper` tap controls, no free-text entry) |

## Feature 03 - Point Values

| AC group | Task | Proof | Result |
|---|---|---|---|
| Default 10; change 1..100 applies to unmatched games | P3-02, P3-03 | D, A | PASS: `PointRuleValidationTests` (1..100 range), `PointRulesEndpointsTests.GivenAChangedLeagueDefault_WhenUpdatingSettings_ThenUnlockedWeeksReResolveImmediately` |
| No rule = default; one rule = its value; multiple = highest priority | P3-02 | D `PointValueResolverTests` x3 | PASS: `PointValueResolverTests` (21 tests incl. the default/single-rule/priority cases) |
| Close spread with no spread = no match | P3-02 | D | PASS: `PointValueResolverTests` (`CloseSpread` rule cases) |
| Daily spread refresh, snapshot at lock | P2-04, P4-02 | A `LockWeekJobTests.GivenADueWeek_WhenTheJobRuns_ThenEachActiveGameHasItsSpreadAndPointValueFrozen`, D `WeekLockerTests` | PASS: `LockWeekJobTests.GivenADueWeek_WhenTheJobRuns_ThenEachActiveGameHasItsSpreadAndPointValueFrozen`, `WeekLockerTests` |
| Commissioner-only weighting | P3-03 | A: no member endpoint exists | PASS: route inventory (`RouteInventoryTests`) — point-rule mutation routes are all `RequireLeagueCommissioner()` |
| Override wins; per-week only | P3-02, P3-03 | D, A | PASS: `PointValueResolverTests` (override precedence), `PointRulesEndpointsTests.GivenAnOverride_WhenPuttingAndClearing_ThenTheResolvedValueTracksIt` |
| Point value visible and elevated badge | P4-03 | UI | PASS: screenshot `p4-03-in-progress-375.png` |
| Frozen after lock | P3-03, P4-02 | A `PostLockMutationTests` (409 for generate/add/remove/week-rules/override, after the job and in the pre-job window), A `LockWeekJobTests.GivenALockedWeek_WhenAPointRuleAndTheLineChange_ThenTheFrozenValuesDoNotMove` | PASS: `PostLockMutationTests`, `LockWeekJobTests.GivenALockedWeek_WhenAPointRuleAndTheLineChange_ThenTheFrozenValuesDoNotMove`, `PointRulesEndpointsTests.GivenALockedWeek_WhenSettingAnOverride_ThenItIs409` |
| Mid-week change updates submitted members' view, picks valid | P3-03 | A `PointRulesChange_KeepsPicks` | PASS: `PointRulesEndpointsTests.GivenAnExistingPick_WhenReResolvingPointValues_ThenThePickIsUnaffected` (real method name — `PointRulesChange_KeepsPicks` was never the actual name) |

## Feature 04 - Weekly Picks

| AC group | Task | Proof | Result |
|---|---|---|---|
| Picks page lists teams, rank, kickoff, points | P4-03 | UI | PASS: screenshot `p4-03-in-progress-375.png` |
| Tap picks, other unmarked, re-tap is no-op | P4-01, P4-03 | A `SetPickTests`, UI | PASS: `SetPickTests`; screenshot `p4-03-in-progress-375.png` |
| Auto-save with indicator; failure reverts | P4-03 | UI (simulate offline) | PASS: screenshot `p4-03-offline-revert-375.png` (`?failNextPick=1` fake-API flag) |
| Submit only when all picked; remaining count shown | P4-01, P4-03 | A `SubmitTests`, UI | PASS: `SubmitTests`; screenshot `p4-03-in-progress-375.png` ("N picks left") |
| Change after submit keeps Submitted | P4-01 | D `SubmissionStatusTests` | PASS: `SubmissionStatusTests` |
| Past weeks read-only | P4-01 | A 409 on old week | PASS: `SetPickTests`/`SubmitTests` `WeekNotCurrent` 409 cases; screenshot `p4-03-past-week-375.png` |
| Game added reverts to In Progress, highlighted, notified | P4-04, P7-03 | A `GameAddedTests`, UI, A `NotificationTests.GamesAdded` | PASS: `GameAddedTests`; `EventNotificationTests.GivenSubmittedMembers_WhenGamesAreAddedByRegeneration_ThenEachGetsOneCoalescedMessage` (real class — `NotificationTests` was never real) |
| Game removed keeps its pick row, counts update, member flagged only if they had a pick on it | P4-04 | A `GameRemovedTests` | PASS: `GameRemovedTests` |
| Server-side lock enforcement | P4-01, P4-02 | A `LockEnforcementTests` | PASS: `LockEnforcementTests` |
| Unpicked at lock = Incomplete and 0 points | P4-02, P5-01 | D `WeekLockerTests`, D `WeekScorerTests.NoPickScoresZero` | PASS: `WeekLockerTests`, `WeekScorerTests.GivenAMemberDidNotPick_WhenScoring_ThenTheyEarnZeroForThatGame` (real method name — `NoPickScoresZero` was never the actual name) |
| Server-side lock enforcement | P4-01, P4-02 | A `LockEnforcementTests`, A `PostLockMutationTests` | PASS: `LockEnforcementTests`, `PostLockMutationTests` |
| Unpicked at lock = Incomplete and 0 points | P4-02, P5-01 | D `WeekLockerTests`, A `LockWeekJobTests.GivenASubmitterAndAPartialPicker_WhenTheJobRuns_ThenOneIsLockedAndTheOtherIncomplete`, D `WeekScorerTests.NoPickScoresZero` | PASS: `WeekLockerTests`, `LockWeekJobTests.GivenASubmitterAndAPartialPicker_WhenTheJobRuns_ThenOneIsLockedAndTheOtherIncomplete`, `WeekScorerTests.GivenAMemberDidNotPick_WhenScoring_ThenTheyEarnZeroForThatGame` |
| Picks hidden before lock, visible after | P4-01 | A `PicksVisibilityTests` | PASS: `PicksVisibilityTests` |
| Status values on home; commissioner roster | P4-01, P1-02 | A `FullWeekSimulationTests` (Submitted x4, InProgress 5 of 7, NotStarted on the real roster route), UI | PASS: `FullWeekSimulationTests`; screenshot `p1-02-members-375.png` |
| 44px targets, sticky submit, 2 s interactive | P4-03, P0-04 | UI, spike measurement | PASS: `Implementation/spikes/wasm-load-time.md` ("P4-03: Picks page repeat load", 310-477 ms cold / 337-352 ms warm, both under the 2 s budget); screenshot `p4-03-in-progress-375.png` (44px targets, sticky `SubmitFooter`) |

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
| Weekly total; Complete flag | P5-01 | A `FullWeekSimulationTests` (six members across six snapshots, a void and an override, week Complete), D `WeekScorerTests.GivenSeveralGames_WhenScoring_ThenTheWeeklyTotalIsTheSum...`, `...GivenEveryActiveGameFinalWithAWinner...`, `...GivenOneGameStillToPlay...` |
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
| Trend indicator; none on first week | P5-03 | D `StandingsCalculatorTests.GivenTwoSnapshotWeeks_...` / `GivenOnlyOneSnapshotWeek_...`, A `LeaderboardEndpointsTests.GivenTwoCompletedWeeks_...`, A `FullWeekSimulationTests` (Up/Down/Same off two genuinely scored weeks) |
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
| Works in iOS standalone | P0-04, P8-03 | Manual on iPhone - steps in `Implementation/screenshots/e2e/README.md` (Manual pending, operator) |
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
| Catalog #1/#3 (member reminders) and #2 (commissioner summary, only when someone unsubmitted, names listed) | P7-03 | A `ReminderJobTests`; A `FullWeekSimulationTests` (all three fired by the real scheduler at 20:00/21:00/lock-1h ET, recipients and body text asserted) |
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
