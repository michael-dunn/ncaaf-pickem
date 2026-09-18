using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Fixtures;

/// <summary>
/// Validates the shape of the Week 7, 2026 fixture data set against the exact category counts in
/// <c>05-Conventions.md</c>'s testing section and the P2-05 task card, through the fixture
/// providers rather than by re-parsing JSON, so a shape bug the providers would also hit fails
/// here first.
/// </summary>
public sealed class FixtureLoaderTests
{
    private readonly IReferenceDataProvider _referenceData = new FixtureReferenceDataProvider();

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenItHasSixteenGames()
    {
        IReadOnlyList<ProviderGame> games = await _referenceData.GetGamesAsync(2026, 7);

        games.Should().HaveCount(16);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenTwelveGamesAreSaturdayFbsVersusFbs()
    {
        (IReadOnlyList<ProviderGame> games, IReadOnlyList<ProviderTeam> teams) = await LoadScheduleAsync();
        Dictionary<int, ProviderTeam> teamsById = teams.ToDictionary(t => t.CfbdId);

        // The 12 literal-Saturday FBS games are 700001..700012. 700016 (the Friday-Pacific game)
        // is also Saturday-Eastern and FBS-vs-FBS but is counted separately per the task card.
        List<ProviderGame> theTwelve = [.. games.Where(g => g.CfbdGameId is >= 700001 and <= 700012)];

        theTwelve.Should().HaveCount(12);
        theTwelve.Should().OnlyContain(g =>
            SeasonCalendar.IsSaturdayEastern(AsUtc(g.KickoffUtc))
            && teamsById[g.HomeCfbdTeamId].Classification == TeamClassification.Fbs
            && teamsById[g.AwayCfbdTeamId].Classification == TeamClassification.Fbs);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenTwoGamesInvolveAnFcsTeam()
    {
        (IReadOnlyList<ProviderGame> games, IReadOnlyList<ProviderTeam> teams) = await LoadScheduleAsync();
        Dictionary<int, ProviderTeam> teamsById = teams.ToDictionary(t => t.CfbdId);

        int fcsGameCount = games.Count(g =>
            teamsById[g.HomeCfbdTeamId].Classification == TeamClassification.Fcs
            || teamsById[g.AwayCfbdTeamId].Classification == TeamClassification.Fcs);

        fcsGameCount.Should().Be(2);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenOneGameIsFridayEasternAndExcluded()
    {
        IReadOnlyList<ProviderGame> games = await _referenceData.GetGamesAsync(2026, 7);

        ProviderGame fridayGame = games.Single(g => g.CfbdGameId == 700015);

        SeasonCalendar.IsSaturdayEastern(AsUtc(fridayGame.KickoffUtc)).Should().BeFalse();
        SeasonCalendar.ToEastern(AsUtc(fridayGame.KickoffUtc)).DayOfWeek.Should().Be(DayOfWeek.Friday);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenOneGameIsFridayPacificButSaturdayEasternAndIncluded()
    {
        (IReadOnlyList<ProviderGame> games, IReadOnlyList<ProviderTeam> teams) = await LoadScheduleAsync();
        Dictionary<int, ProviderTeam> teamsById = teams.ToDictionary(t => t.CfbdId);

        ProviderGame fridayPacificGame = games.Single(g => g.CfbdGameId == 700016);
        DateTimeOffset eastern = SeasonCalendar.ToEastern(AsUtc(fridayPacificGame.KickoffUtc));

        SeasonCalendar.IsSaturdayEastern(AsUtc(fridayPacificGame.KickoffUtc)).Should().BeTrue();
        eastern.Hour.Should().Be(0);
        eastern.Minute.Should().Be(30);
        teamsById[fridayPacificGame.HomeCfbdTeamId].Classification.Should().Be(TeamClassification.Fbs);
        teamsById[fridayPacificGame.AwayCfbdTeamId].Classification.Should().Be(TeamClassification.Fbs);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenAtLeastTwoOfTheTwelveAreConferenceGames()
    {
        IReadOnlyList<ProviderGame> games = await _referenceData.GetGamesAsync(2026, 7);

        int conferenceGameCount = games.Count(g => g.CfbdGameId is >= 700001 and <= 700012 && g.IsConferenceGame);

        conferenceGameCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenMichiganAndTexasPlayEachOther()
    {
        (IReadOnlyList<ProviderGame> games, IReadOnlyList<ProviderTeam> teams) = await LoadScheduleAsync();
        int michiganId = teams.Single(t => t.School == "Michigan").CfbdId;
        int texasId = teams.Single(t => t.School == "Texas").CfbdId;

        games.Should().Contain(g =>
            (g.HomeCfbdTeamId == michiganId && g.AwayCfbdTeamId == texasId)
            || (g.HomeCfbdTeamId == texasId && g.AwayCfbdTeamId == michiganId));
    }

    [Fact]
    public async Task GivenWeek7Schedule_WhenLoaded_ThenMarylandAndRutgersPlayEachOther()
    {
        (IReadOnlyList<ProviderGame> games, IReadOnlyList<ProviderTeam> teams) = await LoadScheduleAsync();
        int marylandId = teams.Single(t => t.School == "Maryland").CfbdId;
        int rutgersId = teams.Single(t => t.School == "Rutgers").CfbdId;

        games.Should().Contain(g =>
            (g.HomeCfbdTeamId == marylandId && g.AwayCfbdTeamId == rutgersId)
            || (g.HomeCfbdTeamId == rutgersId && g.AwayCfbdTeamId == marylandId));
    }

    [Fact]
    public async Task GivenRankings_WhenLoaded_ThenExactlyFourRankedTeamsAppearInTheSaturdayFbsGames()
    {
        IReadOnlyList<ProviderGame> games = await _referenceData.GetGamesAsync(2026, 7);
        IReadOnlyList<ProviderRanking> rankings = await _referenceData.GetRankingsAsync(2026, 7);

        HashSet<int> saturdayFbsTeamIds =
        [
            .. games.Where(g => g.CfbdGameId is >= 700001 and <= 700012)
                .SelectMany(g => new[] { g.HomeCfbdTeamId, g.AwayCfbdTeamId }),
        ];

        int rankedCount = rankings.Select(r => r.CfbdTeamId).Distinct().Count(saturdayFbsTeamIds.Contains);

        rankedCount.Should().Be(4);
        rankings.Should().OnlyContain(r => r.Poll == "AP");
    }

    [Fact]
    public async Task GivenLines_WhenLoaded_ThenTenOfTheTwelveSaturdayFbsGamesHaveASpread()
    {
        IReadOnlyList<ProviderLine> lines = await _referenceData.GetLinesAsync(2026, 7);

        HashSet<long> gameIdsWithLines = [.. lines.Where(l => l.Spread is not null).Select(l => l.CfbdGameId)];

        gameIdsWithLines.Should().HaveCount(10);
        gameIdsWithLines.Should().OnlyContain(id => id >= 700001 && id <= 700012);
    }

    [Fact]
    public async Task GivenLines_WhenLoaded_ThenAtLeastOneSpreadIsCloseAndOneIsAwayFavored()
    {
        IReadOnlyList<ProviderLine> lines = await _referenceData.GetLinesAsync(2026, 7);

        lines.Should().Contain(l => l.Spread != null && Math.Abs(l.Spread.Value) < 3m);
        lines.Should().Contain(l => l.Spread != null && l.Spread.Value > 0m);
    }

    [Fact]
    public async Task GivenConferences_WhenLoaded_ThenEveryScheduleConferenceIsPresent()
    {
        IReadOnlyList<ProviderConference> conferences = await _referenceData.GetConferencesAsync(2026);
        IReadOnlyList<ProviderTeam> teams = await _referenceData.GetTeamsAsync(2026);

        HashSet<int> conferenceIds = [.. conferences.Select(c => c.CfbdId)];

        teams.Should().OnlyContain(t => t.ConferenceCfbdId == null || conferenceIds.Contains(t.ConferenceCfbdId.Value));
    }

    [Fact]
    public async Task GivenTeams_WhenLoaded_ThenTheElevenRealCapturedTeamAliasesArePresent()
    {
        // Real CFBD alternateNames data from tests/NcaafPickEm.Fixtures/Real/cfbd-teams-fbs.json.
        IReadOnlyList<ProviderTeam> teams = await _referenceData.GetTeamsAsync(2026);

        ProviderTeam appState = teams.Single(t => t.School == "App State");
        appState.AlternateNames.Should().Contain("Appalachian State");
        appState.CfbdId.Should().Be(2026);
    }

    [Fact]
    public async Task GivenSixSnapshots_WhenLoaded_ThenStatusesProgressFromScheduledToAllFinal()
    {
        var snapshotState = new FixtureSnapshotState();
        var liveScores = new FixtureLiveScoreProvider(snapshotState);
        var saturday = new DateOnly(2026, 10, 17);

        snapshotState.Set(1);
        IReadOnlyList<LiveScoreUpdate> snapshot1 = await liveScores.GetScoresAsync(saturday);
        snapshot1.Should().HaveCount(15);
        snapshot1.Where(g => g.HomeName != "USC").Should().OnlyContain(g => g.Status == GameStatus.Scheduled);

        snapshotState.Set(6);
        IReadOnlyList<LiveScoreUpdate> snapshot6 = await liveScores.GetScoresAsync(saturday);
        snapshot6.Should().OnlyContain(g => g.Status == GameStatus.Final);
    }

    [Fact]
    public async Task GivenSnapshotFive_WhenLoaded_ThenExactlyOneGameIsATieNeedingReview()
    {
        var snapshotState = new FixtureSnapshotState();
        snapshotState.Set(5);
        var liveScores = new FixtureLiveScoreProvider(snapshotState);
        var saturday = new DateOnly(2026, 10, 17);

        IReadOnlyList<LiveScoreUpdate> snapshot5 = await liveScores.GetScoresAsync(saturday);

        List<LiveScoreUpdate> ties = [.. snapshot5.Where(g =>
            g.Status == GameStatus.Final && g.HomeScore == g.AwayScore)];

        ties.Should().ContainSingle();
        ties[0].HomeName.Should().Be("Iowa State");
    }

    [Fact]
    public async Task GivenSnapshotSix_WhenLoaded_ThenTheLateGameFinishesAfterMidnightEastern()
    {
        var snapshotState = new FixtureSnapshotState();
        snapshotState.Set(6);
        var liveScores = new FixtureLiveScoreProvider(snapshotState);
        var saturday = new DateOnly(2026, 10, 17);

        IReadOnlyList<LiveScoreUpdate> snapshot6 = await liveScores.GetScoresAsync(saturday);
        LiveScoreUpdate lateGame = snapshot6.Single(g => g.HomeName == "San José State");

        lateGame.Status.Should().Be(GameStatus.Final);

        // The kickoff itself (22:30 ET) is before midnight; only the fixture's snapshot timestamp
        // (checked directly against the raw JSON here, since LiveScoreUpdate has no snapshot
        // field of its own) proves the Final lands after midnight Eastern.
        var snapshotFile = FixtureLoader.Read<SnapshotEnvelope>("Week7_2026/scores/snapshot-6.json");
        DateTimeOffset snapshotUtc = new(DateTime.SpecifyKind(snapshotFile.SnapshotUtc, DateTimeKind.Utc));
        DateTimeOffset snapshotEastern = SeasonCalendar.ToEastern(snapshotUtc);

        snapshotEastern.Date.Should().Be(new DateTime(2026, 10, 18));
    }

    [Fact]
    public void GivenInfluenceExampleFixture_WhenLoaded_ThenItMatchesTheOverviewWorkedExample()
    {
        InfluenceExampleFile example = FixtureLoader.Read<InfluenceExampleFile>("influence-example.json");

        example.Members.Should().Equal("Michael", "Alyson", "Dance", "Alex", "Daniel");
        example.Games.Should().HaveCount(2);

        InfluenceExampleGame michiganTexas = example.Games.Single(g => g.Label == "Michigan vs Texas");
        michiganTexas.Picks["Michigan"].Should().Equal("Michael", "Alyson", "Alex", "Daniel");
        michiganTexas.Picks["Texas"].Should().Equal("Dance");

        InfluenceExampleGame marylandRutgers = example.Games.Single(g => g.Label == "Maryland vs Rutgers");
        marylandRutgers.Picks["Maryland"].Should().Equal("Michael", "Alyson", "Dance", "Daniel");
        marylandRutgers.Picks["Rutgers"].Should().Equal("Alex");

        example.ExpectedDashboards["Dance"].Single(g => g.Label == "Michigan vs Texas").OppositePicks
            .Should().Equal("Michael", "Alyson", "Alex", "Daniel");
        example.ExpectedDashboards["Alyson"].Single(g => g.Label == "Michigan vs Texas").OppositePicks
            .Should().Equal("Dance");
    }

    private async Task<(IReadOnlyList<ProviderGame> Games, IReadOnlyList<ProviderTeam> Teams)> LoadScheduleAsync()
    {
        IReadOnlyList<ProviderGame> games = await _referenceData.GetGamesAsync(2026, 7);
        IReadOnlyList<ProviderTeam> teams = await _referenceData.GetTeamsAsync(2026);
        return (games, teams);
    }

    private static DateTimeOffset AsUtc(DateTime kickoffUtc) =>
        new(DateTime.SpecifyKind(kickoffUtc, DateTimeKind.Utc));

    private sealed record SnapshotEnvelope(DateTime SnapshotUtc);

    private sealed record InfluenceExampleFile(
        IReadOnlyList<string> Members,
        IReadOnlyList<InfluenceExampleGame> Games,
        Dictionary<string, IReadOnlyList<InfluenceExampleDashboardGame>> ExpectedDashboards);

    private sealed record InfluenceExampleGame(
        string Label,
        string HomeTeam,
        string AwayTeam,
        int PointValue,
        Dictionary<string, IReadOnlyList<string>> Picks);

    private sealed record InfluenceExampleDashboardGame(string Label, string MyTeam, IReadOnlyList<string> OppositePicks);
}
