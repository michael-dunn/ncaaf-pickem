using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Domain.Stats;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Contracts.Stats;

namespace NcaafPickEm.Domain.Tests.Stats;

/// <summary>
/// <c>04-Domain-Algorithms.md</c> section 13 (Feature 14): counted games, accuracy, underdog hits,
/// category ranking, best and worst weeks, late joiners and former members.
/// </summary>
public sealed class MemberStatsCalculatorTests
{
    private static readonly Guid Michael = TestIds.Of("membership:Michael");
    private static readonly Guid Alyson = TestIds.Of("membership:Alyson");
    private static readonly Guid Alex = TestIds.Of("membership:Alex");
    private static readonly Guid Dance = TestIds.Of("membership:Dance");

    private static readonly Guid Home = TestIds.Of("team:Michigan");
    private static readonly Guid Away = TestIds.Of("team:Texas");

    [Fact]
    public void GivenPendingAndUnpickedGames_WhenComputingAccuracy_ThenOnlyDecidedGamesCountAndAMissingPickIsAMiss()
    {
        StatsGame decided = Game(1, winner: Home, weekComplete: false);
        StatsGame unpicked = Game(1, winner: Away, weekComplete: false);
        StatsGame pending = Game(1, winner: null, weekComplete: false);

        LeagueStats stats = MemberStatsCalculator.LeagueStats(
            [Member(Michael, "Michael")],
            [decided, unpicked, pending],
            [Pick(Michael, decided, Home), Pick(Michael, pending, Home)],
            [Result(Michael, 1)],
            viewerMembershipId: Michael);

        StatRow row = stats.Accuracy.Single();
        row.Hits.Should().Be(1);
        row.Total.Should().Be(2, "the denominator is counted games, not picks made; a pending game is not counted");
        row.IsMe.Should().BeTrue();
        stats.ThroughWeek.Should().Be(1);
        stats.IsProvisional.Should().BeTrue("week 1 is still being played");
    }

    [Fact]
    public void GivenNoDecidedGames_WhenComputingLeagueStats_ThenThroughWeekIsNull()
    {
        LeagueStats stats = MemberStatsCalculator.LeagueStats(
            [Member(Michael, "Michael")],
            [Game(1, winner: null)],
            [],
            [Result(Michael, 1)],
            viewerMembershipId: null);

        stats.ThroughWeek.Should().BeNull("the page shows its empty state before any game is counted");
        stats.Accuracy.Single().Total.Should().Be(0);
    }

    [Theory]
    [InlineData(-3.5, false)] // home favored: the away team is the underdog
    [InlineData(7.0, true)] // home getting points: the home team is the underdog
    public void GivenALine_WhenTheUnderdogWins_ThenPickingItIsAnUnderdogHit(double spread, bool homeIsUnderdog)
    {
        Guid underdog = homeIsUnderdog ? Home : Away;
        Guid favorite = homeIsUnderdog ? Away : Home;
        StatsGame upset = Game(1, winner: underdog, spread: (decimal)spread);
        StatsGame chalk = Game(1, winner: favorite, spread: (decimal)spread);

        LeagueStats stats = MemberStatsCalculator.LeagueStats(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [upset, chalk],
            [Pick(Michael, upset, underdog), Pick(Alyson, upset, favorite), Pick(Alyson, chalk, underdog)],
            [Result(Michael, 1), Result(Alyson, 1)],
            viewerMembershipId: null);

        Row(stats.Underdog, Michael).Should().Match<StatRow>(row => row.Hits == 1 && row.Total == 1);
        Row(stats.Underdog, Alyson).Should().Match<StatRow>(row => row.Hits == 0 && row.Total == 1,
            "a favorite's win is not an underdog win, whoever picked the dog");
    }

    [Fact]
    public void GivenNoLineOrAPickEm_WhenComputingUnderdogHits_ThenTheGameIsExcluded()
    {
        StatsGame noLine = Game(1, winner: Away, spread: null);
        StatsGame pickEm = Game(1, winner: Away, spread: 0m);

        LeagueStats stats = MemberStatsCalculator.LeagueStats(
            [Member(Michael, "Michael")],
            [noLine, pickEm],
            [Pick(Michael, noLine, Away), Pick(Michael, pickEm, Away)],
            [Result(Michael, 1)],
            viewerMembershipId: null);

        stats.Underdog.Single().Total.Should().Be(0);
        stats.Accuracy.Single().Hits.Should().Be(2, "both still count for accuracy");
    }

    [Fact]
    public void GivenEqualPercentages_WhenRanking_ThenTiesShareARankAndTheNextSkips()
    {
        StatsGame[] week1 = [Game(1, Home), Game(1, Home), Game(1, Home), Game(1, Home), Game(1, Home), Game(1, Home)];
        StatsGame[] week2 = [Game(2, Home), Game(2, Home), Game(2, Home)];

        // Michael 2/3 (joined week 2), Alyson 4/6, Alex 6/9, Dance 9/9.
        LeagueStats stats = MemberStatsCalculator.LeagueStats(
            [Member(Michael, "Michael", joinedWeek: 2), Member(Alyson, "Alyson"), Member(Alex, "Alex"), Member(Dance, "Dance")],
            [.. week1, .. week2],
            [
                .. week2.Take(2).Select(game => Pick(Michael, game, Home)),
                .. week1.Take(4).Select(game => Pick(Alyson, game, Home)),
                .. week1.Take(6).Select(game => Pick(Alex, game, Home)),
                .. week1.Concat(week2).Select(game => Pick(Dance, game, Home)),
            ],
            [Result(Michael, 2), Result(Alyson, 1), Result(Alex, 1), Result(Alex, 2), Result(Dance, 1), Result(Dance, 2)],
            viewerMembershipId: null);

        stats.Accuracy.Select(row => row.MembershipId).Should().Equal(Dance, Alex, Alyson, Michael);
        stats.Accuracy.Select(row => row.Rank).Should().Equal([1, 2, 2, 2], "2/3, 4/6 and 6/9 are the same percentage");
        Row(stats.Accuracy, Michael).Total.Should().Be(3, "a late joiner is measured on their own weeks only");
        Row(stats.Accuracy, Alyson).Total.Should().Be(6, "a member with no result row for a week did not play it");
    }

    [Fact]
    public void GivenCompleteWeeks_WhenBuildingAProfile_ThenBestAndWorstWeeksPreferTheMostRecentTie()
    {
        StandingsWeekResult[] results =
        [
            Result(Michael, 1, points: 30, correct: 8),
            Result(Michael, 2, points: 10, correct: 5),
            Result(Michael, 3, points: 30, correct: 5),
            Result(Michael, 4, points: 99, correct: 12, complete: false),
            Result(Alyson, 3, points: 40, correct: 9),
        ];

        MemberStatsProfile profile = MemberStatsCalculator.Profile(
            Member(Michael, "Michael"),
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [],
            [],
            results,
            viewerMembershipId: Alyson);

        profile.IsMe.Should().BeFalse();
        profile.MostCorrect.Should().BeEquivalentTo(new WeekHighlight(1, 8, 12, []),
            "the in-progress week 4 is excluded until it is Complete");
        profile.LeastCorrect.Should().BeEquivalentTo(new WeekHighlight(3, 5, 12, [2]));
        profile.MostPoints.Should().BeEquivalentTo(new WeekHighlight(3, 30, 12, [1]));
        profile.LeastPoints.Should().BeEquivalentTo(new WeekHighlight(2, 10, 12, []));

        profile.Weeks.Select(week => week.Week).Should().Equal(1, 2, 3, 4);
        profile.Weeks.Single(week => week.Week == 3).Rank.Should().Be(2, "Alyson outscored them in week 3");
        profile.Weeks.Single(week => week.Week == 4).IsComplete.Should().BeFalse();
    }

    [Fact]
    public void GivenAFormerMember_WhenBuildingTheirProfile_ThenTheyHaveStatsButNoRank()
    {
        StatsGame game = Game(1, winner: Home);

        MemberStatsProfile profile = MemberStatsCalculator.Profile(
            Member(Dance, "Dance", isFormer: true),
            [Member(Michael, "Michael")],
            [game],
            [Pick(Dance, game, Home), Pick(Michael, game, Home)],
            [Result(Dance, 1), Result(Michael, 1)],
            viewerMembershipId: Michael);

        profile.IsFormer.Should().BeTrue();
        profile.Accuracy.Should().Be(new MemberStat(Rank: null, RankedCount: 1, Hits: 1, Total: 1));
        profile.Weeks.Should().ContainSingle();
    }

    [Fact]
    public void GivenAnActiveMember_WhenBuildingTheirProfile_ThenEachCategoryCarriesTheirLeagueRank()
    {
        StatsGame upset = Game(1, winner: Away, spread: -7m);

        MemberStatsProfile profile = MemberStatsCalculator.Profile(
            Member(Michael, "Michael"),
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [upset],
            [Pick(Alyson, upset, Away), Pick(Michael, upset, Home)],
            [Result(Michael, 1), Result(Alyson, 1)],
            viewerMembershipId: Michael);

        profile.IsMe.Should().BeTrue();
        profile.Accuracy.Should().Be(new MemberStat(2, 2, 0, 1));
        profile.Underdog.Should().Be(new MemberStat(2, 2, 0, 1));
    }

    private static StatRow Row(StatRow[] rows, Guid membershipId) =>
        rows.Single(row => row.MembershipId == membershipId);

    private static StandingsMember Member(Guid id, string name, int joinedWeek = 1, bool isFormer = false) =>
        new(id, name, isFormer, joinedWeek);

    private static StatsGame Game(int week, Guid? winner, decimal? spread = null, bool weekComplete = true) =>
        new(Guid.NewGuid(), week, Home, Away, spread, winner, weekComplete);

    private static StatsPick Pick(Guid membershipId, StatsGame game, Guid teamId) =>
        new(membershipId, game.GameSetGameId, teamId);

    private static StandingsWeekResult Result(
        Guid membershipId,
        int week,
        int points = 0,
        int correct = 0,
        bool complete = true) =>
        new(membershipId, week, points, correct, ActiveGameCount: 12, complete);
}
