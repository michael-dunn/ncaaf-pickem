using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Shared.Enums;
using static NcaafPickEm.Domain.Tests.GameSets.GameInfoBuilder;
using static NcaafPickEm.Domain.Tests.GameSets.GameSetTimes;

namespace NcaafPickEm.Domain.Tests.GameSets;

/// <summary>
/// Feature 02 acceptance criteria, one test per criterion, against
/// <c>04-Domain-Algorithms.md</c> section 2.
/// </summary>
public sealed class GameSetGeneratorTests
{
    private const int Week = 7;

    // --- Union and distinct ------------------------------------------------------------------

    [Fact]
    public void GivenSeveralRules_WhenGenerating_ThenTheResultIsTheirUnionWithEachGameOnce()
    {
        GameSetGenerationRequest request = Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),      // matched by all three rules
                Game("Baylor", "Texas").InConference("Big 12"),      // matched by Top 25 only
                Game("Duke", "Wake Forest").InConference("SEC"),     // matched by the conference rule only
                Game("Kent State", "Toledo").InConference("MAC"),    // matched by nothing
            ],
            rules: [Top25Rule(), ConferenceRule("SEC"), TeamRule("Alabama")],
            rankings: [Poll(Week, PollFetchedTuesday, "Alabama", "Georgia", "Texas")]);

        GenerationResult result = GameSetGenerator.Generate(request);

        result.Games.Select(game => game.GameId).Should().BeEquivalentTo(
        [
            GameId("Georgia", "Alabama"),
            GameId("Baylor", "Texas"),
            GameId("Duke", "Wake Forest"),
        ]);
        result.Games.Select(game => game.Source).Should().AllBeEquivalentTo(GameSetGameSource.Rule);
    }

    // --- Top 25 ------------------------------------------------------------------------------

    [Fact]
    public void GivenTwoPollsForTheWeek_WhenGenerating_ThenTheLatestFetchWinsAndNoFallbackIsFlagged()
    {
        GameSetGenerationRequest request = Request(
            games:
            [
                Game("Georgia", "Alabama"),
                Game("Baylor", "Texas"),
            ],
            rules: [Top25Rule()],
            rankings:
            [
                Poll(Week, PollFetchedSunday, "Texas"),
                Poll(Week, PollFetchedTuesday, "Alabama"),
            ]);

        GenerationResult result = GameSetGenerator.Generate(request);

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Georgia", "Alabama"));
        result.UsedFallbackRankings.Should().BeFalse();
    }

    [Fact]
    public void GivenNoPollForTheWeek_WhenGenerating_ThenThePriorWeekPollIsUsedAndFlagged()
    {
        GameSetGenerationRequest request = Request(
            games:
            [
                Game("Georgia", "Alabama"),
                Game("Baylor", "Texas"),
            ],
            rules: [Top25Rule()],
            rankings:
            [
                Poll(Week - 2, PriorPollFetched, "Alabama"),
                Poll(Week - 1, PriorPollFetched, "Texas"),
            ]);

        GenerationResult result = GameSetGenerator.Generate(request);

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Baylor", "Texas"));
        result.UsedFallbackRankings.Should().BeTrue();
    }

    [Fact]
    public void GivenNoTop25Rule_WhenOnlyAnOldPollExists_ThenFallbackIsNotFlagged()
    {
        GameSetGenerationRequest request = Request(
            games: [Game("Georgia", "Alabama").InConference("SEC")],
            rules: [ConferenceRule("SEC")],
            rankings: [Poll(Week - 1, PriorPollFetched, "Alabama")]);

        GenerationResult result = GameSetGenerator.Generate(request);

        result.Games.Should().ContainSingle();
        result.UsedFallbackRankings.Should().BeFalse();
    }

    // --- Conference --------------------------------------------------------------------------

    [Fact]
    public void GivenAConferenceRule_WhenConferenceGamesOnlyIsOff_ThenEitherTeamQualifies()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: ConferenceSlate(),
            rules: [ConferenceRule("SEC")]));

        result.Games.Select(game => game.GameId).Should().BeEquivalentTo(
        [
            GameId("Georgia", "Alabama"),
            GameId("Notre Dame", "LSU"),
        ]);
    }

    [Fact]
    public void GivenAConferenceRule_WhenConferenceGamesOnlyIsOn_ThenBothTeamsMustQualify()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: ConferenceSlate(),
            rules: [ConferenceRule("SEC", conferenceGamesOnly: true)]));

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Georgia", "Alabama"));
    }

    // --- Specific team -----------------------------------------------------------------------

    [Fact]
    public void GivenATeamRule_WhenThatTeamHasAByeWeek_ThenNoGameIsAdded()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama"),
                Game("Duke", "Wake Forest"),
            ],
            rules: [TeamRule("Navy")]));

        result.Games.Should().BeEmpty();
        result.LockAtUtc.Should().BeNull();
    }

    // --- Saturday Eastern --------------------------------------------------------------------

    [Fact]
    public void GivenFridayKickoffs_WhenGenerating_ThenOnlyTheFridayPacificGameIsSaturdayEastern()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Boise State", "Fresno State").InConference("MWC").OnFridayPacific(),
                Game("Memphis", "Tulane").InConference("MWC").OnFridayEastern(),
            ],
            rules: [ConferenceRule("MWC")]));

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Boise State", "Fresno State"));
        result.LockAtUtc.Should().Be(FridayNightPacific);
    }

    // --- Classification ----------------------------------------------------------------------

    [Fact]
    public void GivenAnFcsOpponent_WhenGenerating_ThenTheGameIsNeverIncluded()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Mercer", "Alabama").HomeInConference("SEC").AwayIsFcs(),
                Game("Samford", "Auburn").HomeInConference("SEC").AwayIsFcs(),
                Game("Georgia", "Florida").InConference("SEC"),
            ],
            rules: [ConferenceRule("SEC"), TeamRule("Alabama")]));

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Georgia", "Florida"));
    }

    [Fact]
    public void GivenAnFcsHomeTeam_WhenGenerating_ThenTheGameIsNeverIncluded()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: [Game("Alabama", "Alabama State").HomeIsFcs().AwayInConference("SEC")],
            rules: [TeamRule("Alabama")]));

        result.Games.Should().BeEmpty();
    }

    // --- Status ------------------------------------------------------------------------------

    [Fact]
    public void GivenPostponedAndCancelledGames_WhenGenerating_ThenNeitherIsIncluded()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC").WithStatus(GameStatus.Postponed),
                Game("Auburn", "LSU").InConference("SEC").WithStatus(GameStatus.Cancelled),
                Game("Duke", "Wake Forest").InConference("SEC").WithStatus(GameStatus.Scheduled),
            ],
            rules: [ConferenceRule("SEC")]));

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Duke", "Wake Forest"));
    }

    // --- 50-game cap -------------------------------------------------------------------------

    [Fact]
    public void GivenFiftyMatchingGames_WhenGenerating_ThenTheCapIsNotExceeded()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: BigSlate(GameSetLimits.MaxGames),
            rules: [ConferenceRule("Everything")]));

        result.Count.Should().Be(50);
        result.ExceedsMax.Should().BeFalse();
    }

    [Fact]
    public void GivenMoreThanFiftyMatchingGames_WhenGenerating_ThenExceedsMaxIsFlaggedAndAllAreReturned()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: BigSlate(GameSetLimits.MaxGames + 1),
            rules: [ConferenceRule("Everything")]));

        result.Count.Should().Be(51);
        result.ExceedsMax.Should().BeTrue();
        result.Games.Should().HaveCount(51, "a preview must show what the rules produced");
    }

    // --- Manual adds and sticky removals -----------------------------------------------------

    [Fact]
    public void GivenAManuallyRemovedGame_WhenRegenerating_ThenItStaysOutOfTheSet()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),
                Game("Auburn", "LSU").InConference("SEC"),
            ],
            rules: [ConferenceRule("SEC")],
            existing:
            [
                new ExistingSetGame(GameId("Georgia", "Alabama"), GameSetGameSource.Rule, IsRemoved: true),
            ]));

        result.Games.Should().ContainSingle()
            .Which.GameId.Should().Be(GameId("Auburn", "LSU"));
        result.Added.Should().BeEquivalentTo([GameId("Auburn", "LSU")]);
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void GivenAManuallyAddedGame_WhenRegenerating_ThenItIsKeptThoughNoRuleMatchesIt()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),
                Game("Kent State", "Toledo").InConference("MAC"),
            ],
            rules: [ConferenceRule("SEC")],
            existing:
            [
                new ExistingSetGame(GameId("Kent State", "Toledo"), GameSetGameSource.Manual),
            ]));

        result.Games.Should().Contain(new GeneratedGame(GameId("Kent State", "Toledo"), GameSetGameSource.Manual));
        result.Added.Should().BeEquivalentTo([GameId("Georgia", "Alabama")]);
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void GivenARuleNowMatchesAManualGame_WhenRegenerating_ThenTheRowStaysManual()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: [Game("Georgia", "Alabama").InConference("SEC")],
            rules: [ConferenceRule("SEC")],
            existing:
            [
                new ExistingSetGame(GameId("Georgia", "Alabama"), GameSetGameSource.Manual),
            ]));

        result.Games.Should().ContainSingle()
            .Which.Source.Should().Be(GameSetGameSource.Manual);
    }

    // --- Regeneration diff -------------------------------------------------------------------

    [Fact]
    public void GivenNarrowedRules_WhenRegenerating_ThenOnlyRuleRowsAreRemovedAndManualRowsSurvive()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),
                Game("Auburn", "LSU").InConference("SEC"),
                Game("Kent State", "Toledo").InConference("MAC"),
                Game("Duke", "Wake Forest").InConference("ACC"),
            ],
            rules: [TeamRule("Alabama"), ConferenceRule("ACC")],
            existing:
            [
                new ExistingSetGame(GameId("Georgia", "Alabama"), GameSetGameSource.Rule),
                new ExistingSetGame(GameId("Auburn", "LSU"), GameSetGameSource.Rule),
                new ExistingSetGame(GameId("Kent State", "Toledo"), GameSetGameSource.Manual),
            ]));

        result.Games.Select(game => game.GameId).Should().BeEquivalentTo(
        [
            GameId("Georgia", "Alabama"),
            GameId("Kent State", "Toledo"),
            GameId("Duke", "Wake Forest"),
        ]);
        result.Added.Should().BeEquivalentTo([GameId("Duke", "Wake Forest")]);
        result.Removed.Should().BeEquivalentTo([GameId("Auburn", "LSU")]);
        result.Removed.Should().NotContain(GameId("Kent State", "Toledo"));
        result.RemovedIneligible.Should().BeEmpty();
    }

    [Fact]
    public void GivenAManualGameThatWasCancelled_WhenRegenerating_ThenItLeavesTheSetAsIneligible()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),
                Game("Kent State", "Toledo").InConference("MAC").WithStatus(GameStatus.Cancelled),
            ],
            rules: [ConferenceRule("SEC")],
            existing:
            [
                new ExistingSetGame(GameId("Georgia", "Alabama"), GameSetGameSource.Rule),
                new ExistingSetGame(GameId("Kent State", "Toledo"), GameSetGameSource.Manual),
            ]));

        result.Games.Select(game => game.GameId).Should().BeEquivalentTo([GameId("Georgia", "Alabama")]);
        result.RemovedIneligible.Should().BeEquivalentTo([GameId("Kent State", "Toledo")]);
        result.Removed.Should().BeEmpty();
    }

    // --- Lock --------------------------------------------------------------------------------

    [Fact]
    public void GivenALockedWeek_WhenGenerating_ThenNothingIsProducedAndTheRefusalIsFlagged()
    {
        GameSetGenerationRequest request = Request(
            games: [Game("Georgia", "Alabama").InConference("SEC")],
            rules: [ConferenceRule("SEC")],
            isLocked: true);

        GenerationResult result = GameSetGenerator.Generate(request);

        result.IsLocked.Should().BeTrue();
        result.Games.Should().BeEmpty();
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.RemovedIneligible.Should().BeEmpty();
        result.LockAtUtc.Should().BeNull();
        result.ExceedsMax.Should().BeFalse();
    }

    [Fact]
    public void GivenALockedWeek_WhenPreviewing_ThenTheRulesAreStillEvaluated()
    {
        GameSetGenerationRequest request = Request(
            games: [Game("Georgia", "Alabama").InConference("SEC")],
            rules: [ConferenceRule("SEC")],
            isLocked: true);

        GenerationResult result = GameSetGenerator.Preview(request);

        result.IsLocked.Should().BeFalse();
        result.Games.Should().ContainSingle();
    }

    // --- Lock time and ordering ---------------------------------------------------------------

    [Fact]
    public void GivenGamesThroughTheDay_WhenGenerating_ThenLockAtUtcIsTheEarliestKickoff()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC").KickingOffAt(SaturdayNight),
                Game("Auburn", "LSU").InConference("SEC").KickingOffAt(SaturdayNoon),
                Game("Duke", "Florida").InConference("SEC").KickingOffAt(SaturdayAfternoon),
            ],
            rules: [ConferenceRule("SEC")]));

        result.LockAtUtc.Should().Be(SaturdayNoon);
        result.Games.Select(game => game.GameId).Should().Equal(
        [
            GameId("Auburn", "LSU"),
            GameId("Duke", "Florida"),
            GameId("Georgia", "Alabama"),
        ]);
    }

    [Fact]
    public void GivenNoMatchingGames_WhenGenerating_ThenLockAtUtcIsNull()
    {
        GenerationResult result = GameSetGenerator.Generate(Request(
            games: [Game("Kent State", "Toledo").InConference("MAC")],
            rules: [ConferenceRule("SEC")]));

        result.Games.Should().BeEmpty();
        result.LockAtUtc.Should().BeNull();
    }

    // --- Preview -----------------------------------------------------------------------------

    [Fact]
    public void GivenCandidateRules_WhenPreviewing_ThenManualAddsAndStickyRemovalsStillApply()
    {
        GameSetGenerationRequest saved = Request(
            games:
            [
                Game("Georgia", "Alabama").InConference("SEC"),
                Game("Auburn", "LSU").InConference("SEC"),
                Game("Kent State", "Toledo").InConference("MAC"),
            ],
            rules: [TeamRule("Alabama")],
            existing:
            [
                new ExistingSetGame(GameId("Kent State", "Toledo"), GameSetGameSource.Manual),
                new ExistingSetGame(GameId("Auburn", "LSU"), GameSetGameSource.Rule, IsRemoved: true),
            ]);

        GenerationResult result = GameSetGenerator.Preview(saved with { Rules = [ConferenceRule("SEC")] });

        result.Games.Select(game => game.GameId).Should().BeEquivalentTo(
        [
            GameId("Georgia", "Alabama"),
            GameId("Kent State", "Toledo"),
        ]);
    }

    // --- Helpers -----------------------------------------------------------------------------

    private static GameSetGenerationRequest Request(
        IEnumerable<GameInfoBuilder> games,
        IEnumerable<RuleInfo>? rules = null,
        IEnumerable<RankingSet>? rankings = null,
        IEnumerable<ExistingSetGame>? existing = null,
        bool isLocked = false) => new()
        {
            Week = Week,
            Games = [.. games.Select(game => game.Build())],
            Rules = rules is null ? [] : [.. rules],
            Rankings = rankings is null ? [] : [.. rankings],
            ExistingGames = existing is null ? [] : [.. existing],
            IsLocked = isLocked,
        };

    private static Guid GameId(string awayTeam, string homeTeam) => TestIds.Of($"{awayTeam} at {homeTeam}");

    private static RuleInfo Top25Rule() => new(GameSetRuleType.Top25);

    private static RuleInfo ConferenceRule(string conference, bool conferenceGamesOnly = false) =>
        new(GameSetRuleType.Conference, ConferenceId: TestIds.Of(conference), ConferenceGamesOnly: conferenceGamesOnly);

    private static RuleInfo TeamRule(string team) => new(GameSetRuleType.Team, TeamId: TestIds.Of(team));

    private static RankingSet Poll(int week, DateTime fetchedUtc, params string[] teams) =>
        new(week, fetchedUtc, teams.Select(TestIds.Of).ToHashSet());

    /// <summary>One all-conference game, one half-conference game, one game elsewhere.</summary>
    private static List<GameInfoBuilder> ConferenceSlate() =>
    [
        Game("Georgia", "Alabama").InConference("SEC"),
        Game("Notre Dame", "LSU").HomeInConference("SEC"),
        Game("Duke", "Wake Forest").InConference("ACC"),
    ];

    private static List<GameInfoBuilder> BigSlate(int count) =>
    [
        .. Enumerable.Range(1, count).Select(index =>
            Game($"Away {index}", $"Home {index}")
                .InConference("Everything")
                .KickingOffAt(SaturdayNoon.AddMinutes(index))),
    ];
}
