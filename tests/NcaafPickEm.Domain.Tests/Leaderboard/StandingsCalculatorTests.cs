using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Leaderboard;

/// <summary>
/// <c>04-Domain-Algorithms.md</c> section 8 (Feature 07): competition ranking, points behind,
/// weekly wins, trend arrows, mid-season joiners, former members, and the grid's cell precedence.
/// </summary>
public sealed class StandingsCalculatorTests
{
    private static readonly Guid Michael = TestIds.Of("membership:Michael");
    private static readonly Guid Alyson = TestIds.Of("membership:Alyson");
    private static readonly Guid Dance = TestIds.Of("membership:Dance");
    private static readonly Guid Alex = TestIds.Of("membership:Alex");
    private static readonly Guid Daniel = TestIds.Of("membership:Daniel");

    private static readonly Guid Home = TestIds.Of("team:Michigan");
    private static readonly Guid Away = TestIds.Of("team:Texas");

    [Fact]
    public void GivenEqualTotals_WhenRankingTheSeason_ThenTiesShareARankAndTheNextOneSkips()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson"), Member(Dance, "Dance"), Member(Alex, "Alex")],
            [
                Result(Michael, week: 1, points: 30),
                Result(Alyson, week: 1, points: 20),
                Result(Dance, week: 1, points: 20),
                Result(Alex, week: 1, points: 10),
            ],
            [],
            viewerMembershipId: null);

        rows.Select(row => row.MembershipId).Should().Equal(Michael, Alyson, Dance, Alex);
        rows.Select(row => row.Rank).Should().Equal(1, 2, 2, 4);
    }

    [Fact]
    public void GivenASeasonLeaderboard_WhenBuilt_ThenEveryRowKnowsHowFarBehindTheLeaderItIs()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [
                Result(Michael, week: 1, points: 30),
                Result(Michael, week: 2, points: 12),
                Result(Alyson, week: 1, points: 20),
            ],
            [],
            viewerMembershipId: Alyson);

        rows[0].TotalPoints.Should().Be(42);
        rows[0].PointsBehind.Should().Be(0);
        rows[0].IsMe.Should().BeFalse();

        rows[1].TotalPoints.Should().Be(20);
        rows[1].PointsBehind.Should().Be(22);
        rows[1].IsMe.Should().BeTrue("the viewer's own row is flagged for highlighting");
    }

    [Fact]
    public void GivenTiedHighScores_WhenCountingWeeklyWins_ThenBothMembersGetTheWin()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson"), Member(Dance, "Dance")],
            [
                // Week 1: Michael and Alyson tie at the top.
                Result(Michael, week: 1, points: 30),
                Result(Alyson, week: 1, points: 30),
                Result(Dance, week: 1, points: 10),

                // Week 2: Michael alone.
                Result(Michael, week: 2, points: 25),
                Result(Alyson, week: 2, points: 5),
                Result(Dance, week: 2, points: 5),
            ],
            [],
            viewerMembershipId: null);

        rows.Single(row => row.MembershipId == Michael).WeeklyWins.Should().Be(2);
        rows.Single(row => row.MembershipId == Alyson).WeeklyWins.Should().Be(1);
        rows.Single(row => row.MembershipId == Dance).WeeklyWins.Should().Be(0);
    }

    [Fact]
    public void GivenAWeekThatIsNotComplete_WhenCountingWeeklyWins_ThenItDoesNotCountYet()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [
                Result(Michael, week: 1, points: 30, isWeekComplete: false),
                Result(Alyson, week: 1, points: 10, isWeekComplete: false),
            ],
            [],
            viewerMembershipId: null);

        rows.Should().OnlyContain(row => row.WeeklyWins == 0);
        rows.Single(row => row.MembershipId == Michael).TotalPoints.Should().Be(
            30, "an in-progress week still counts towards the running total");
    }

    [Fact]
    public void GivenTwoSnapshotWeeks_WhenBuildingTheSeason_ThenTrendsCompareTheLatestTwo()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson"), Member(Dance, "Dance"), Member(Alex, "Alex")],
            [],
            [
                // Through week 1.
                new StandingsSnapshot(1, Michael, Rank: 2, TotalPoints: 20),
                new StandingsSnapshot(1, Alyson, Rank: 1, TotalPoints: 30),
                new StandingsSnapshot(1, Dance, Rank: 3, TotalPoints: 10),

                // Through week 2: Michael climbs, Alyson slips, Dance holds, Alex is new.
                new StandingsSnapshot(2, Michael, Rank: 1, TotalPoints: 50),
                new StandingsSnapshot(2, Alyson, Rank: 2, TotalPoints: 40),
                new StandingsSnapshot(2, Dance, Rank: 3, TotalPoints: 20),
                new StandingsSnapshot(2, Alex, Rank: 4, TotalPoints: 5),
            ],
            viewerMembershipId: null);

        rows.Single(row => row.MembershipId == Michael).Trend.Should().Be(StandingsTrend.Up);
        rows.Single(row => row.MembershipId == Alyson).Trend.Should().Be(StandingsTrend.Down);
        rows.Single(row => row.MembershipId == Dance).Trend.Should().Be(StandingsTrend.Same);
        rows.Single(row => row.MembershipId == Alex).Trend.Should().Be(
            StandingsTrend.None, "a member with no previous snapshot has nothing to compare against");
    }

    [Fact]
    public void GivenOnlyOneSnapshotWeek_WhenBuildingTheSeason_ThenNobodyHasATrend()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [],
            [
                new StandingsSnapshot(1, Michael, Rank: 1, TotalPoints: 30),
                new StandingsSnapshot(1, Alyson, Rank: 2, TotalPoints: 20),
            ],
            viewerMembershipId: null);

        rows.Should().OnlyContain(row => row.Trend == StandingsTrend.None);
    }

    [Fact]
    public void GivenNoSnapshotsAtAll_WhenBuildingTheSeason_ThenNobodyHasATrend()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael")],
            [Result(Michael, week: 1, points: 30)],
            [],
            viewerMembershipId: null);

        rows.Single().Trend.Should().Be(StandingsTrend.None);
    }

    [Fact]
    public void GivenAMidSeasonJoiner_WhenTotallingTheSeason_ThenOnlyTheirOwnWeeksCount()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "Michael"), Member(Daniel, "Daniel", joinedWeek: 3)],
            [
                Result(Michael, week: 1, points: 30),
                Result(Michael, week: 3, points: 10),

                // A stray row for a week before Daniel joined must never reach his total or his wins.
                Result(Daniel, week: 1, points: 99),
                Result(Daniel, week: 3, points: 25),
            ],
            [],
            viewerMembershipId: null);

        SeasonRow daniel = rows.Single(row => row.MembershipId == Daniel);
        daniel.TotalPoints.Should().Be(25);
        daniel.WeeklyWins.Should().Be(1, "he did win week 3, which is one of his own weeks");
        daniel.Rank.Should().Be(2);

        rows.Single(row => row.MembershipId == Michael).TotalPoints.Should().Be(40);
    }

    [Fact]
    public void GivenAFormerMember_WhenBuildingBothLeaderboards_ThenTheyAreOnTheWeekButNotTheSeason()
    {
        StandingsMember[] activeOnly = [Member(Michael, "Michael"), Member(Alyson, "Alyson")];
        StandingsMember[] everyoneWithAResult = [.. activeOnly, Member(Dance, "Dance", isFormer: true)];

        StandingsWeekResult[] results =
        [
            Result(Michael, week: 1, points: 20),
            Result(Alyson, week: 1, points: 10),
            Result(Dance, week: 1, points: 30),
        ];

        SeasonRow[] season = StandingsCalculator.SeasonRows(activeOnly, results, [], viewerMembershipId: null);
        season.Should().HaveCount(2);
        season.Should().NotContain(row => row.MembershipId == Dance);
        season.Single(row => row.MembershipId == Michael).WeeklyWins.Should().Be(
            0, "the week was won by the member who has since left, so nobody else inherits it");

        WeekRow[] week = StandingsCalculator.WeekRows(
            1, everyoneWithAResult, results, isComplete: true, viewerMembershipId: Michael);

        week.Should().HaveCount(3);
        WeekRow former = week.Single(row => row.MembershipId == Dance);
        former.Rank.Should().Be(1);
        former.IsFormer.Should().BeTrue();
        former.IsWinner.Should().BeTrue();
        week.Single(row => row.MembershipId == Michael).IsMe.Should().BeTrue();
    }

    [Fact]
    public void GivenAWeekThatIsNotComplete_WhenBuildingItsRows_ThenNobodyIsMarkedTheWinner()
    {
        WeekRow[] rows = StandingsCalculator.WeekRows(
            7,
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [
                Result(Michael, week: 7, points: 20, correct: 8, activeGames: 12, isWeekComplete: false),
                Result(Alyson, week: 7, points: 20, correct: 9, activeGames: 12, isWeekComplete: false),
            ],
            isComplete: false,
            viewerMembershipId: null);

        rows.Select(row => row.Rank).Should().Equal([1, 1], "tied points share a rank in a week too");
        rows.Should().OnlyContain(row => !row.IsWinner);
        rows.Should().OnlyContain(row => row.Total == 12);
        rows.Single(row => row.MembershipId == Alyson).Correct.Should().Be(9);
    }

    [Fact]
    public void GivenAWeekWithNoResults_WhenBuildingItsRows_ThenTheListIsEmpty()
    {
        StandingsCalculator.WeekRows(
            4,
            [Member(Michael, "Michael")],
            [Result(Michael, week: 5, points: 10)],
            isComplete: false,
            viewerMembershipId: null)
            .Should().BeEmpty();
    }

    [Fact]
    public void GivenAGrid_WhenBuildingCells_ThenOutcomePrecedenceIsVoidedThenNoPickThenPendingThenCorrectness()
    {
        Guid voided = TestIds.Of("gameSetGame:voided");
        Guid pending = TestIds.Of("gameSetGame:pending");
        Guid final = TestIds.Of("gameSetGame:final");

        GridCell[] cells = StandingsCalculator.GridCells(
            [
                new GridGame(voided, IsVoided: true, WinnerTeamId: Home),
                new GridGame(pending, IsVoided: false, WinnerTeamId: null),
                new GridGame(final, IsVoided: false, WinnerTeamId: Home),
            ],
            [Member(Michael, "Michael"), Member(Alyson, "Alyson")],
            [
                new GridPick(Michael, voided, Home),
                new GridPick(Michael, pending, Home),
                new GridPick(Michael, final, Home),
                new GridPick(Alyson, final, Away),

                // Alyson made no pick on the pending game, and a pick from a membership the caller
                // did not list is ignored outright.
                new GridPick(Daniel, final, Home),
            ]);

        cells.Should().HaveCount(6, "one cell per game per member, in game order then member order");

        Cell(cells, voided, Michael).Outcome.Should().Be(
            GridOutcome.Voided, "a voided game beats everything, pick or no pick");
        Cell(cells, voided, Alyson).Outcome.Should().Be(GridOutcome.Voided);
        Cell(cells, pending, Michael).Outcome.Should().Be(GridOutcome.Pending);
        Cell(cells, pending, Alyson).Outcome.Should().Be(
            GridOutcome.NoPick, "no pick beats pending");
        Cell(cells, final, Michael).Outcome.Should().Be(GridOutcome.Correct);
        Cell(cells, final, Michael).TeamId.Should().Be(Home);
        Cell(cells, final, Alyson).Outcome.Should().Be(GridOutcome.Incorrect);
        Cell(cells, pending, Alyson).TeamId.Should().BeNull();

        cells.Should().NotContain(cell => cell.MembershipId == Daniel);
    }

    [Fact]
    public void GivenAWeekThatJustCompleted_WhenComputingItsSnapshot_ThenLaterWeeksAreExcludedAndRanksMatchTheSeason()
    {
        StandingsMember[] members = [Member(Michael, "Michael"), Member(Alyson, "Alyson"), Member(Dance, "Dance")];

        StandingsWeekResult[] results =
        [
            Result(Michael, week: 1, points: 10),
            Result(Alyson, week: 1, points: 30),
            Result(Dance, week: 1, points: 30),

            // Week 2 is still being played and must not leak into the week-1 snapshot.
            Result(Michael, week: 2, points: 40, isWeekComplete: false),
        ];

        StandingsSnapshotRow[] snapshot = StandingsCalculator.ComputeSnapshot(members, results, throughWeek: 1);

        snapshot.Select(row => row.MembershipId).Should().Equal(Alyson, Dance, Michael);
        snapshot.Select(row => row.Rank).Should().Equal(1, 1, 3);
        snapshot.Select(row => row.TotalPoints).Should().Equal(30, 30, 10);

        SeasonRow[] throughWeekOne = StandingsCalculator.SeasonRows(
            members,
            [.. results.Where(result => result.Week <= 1)],
            [],
            viewerMembershipId: null);

        snapshot.Select(row => (row.MembershipId, row.Rank))
            .Should().Equal(throughWeekOne.Select(row => (row.MembershipId, row.Rank)));
    }

    [Fact]
    public void GivenMembersOnTheSameTotal_WhenOrdering_ThenNameDecidesAndTheOrderIsStable()
    {
        SeasonRow[] rows = StandingsCalculator.SeasonRows(
            [Member(Michael, "michael"), Member(Alyson, "Alyson"), Member(Dance, "dance")],
            [],
            [],
            viewerMembershipId: null);

        rows.Select(row => row.DisplayName).Should().Equal("Alyson", "dance", "michael");
        rows.Should().OnlyContain(row => row.Rank == 1 && row.TotalPoints == 0 && row.PointsBehind == 0);
    }

    private static StandingsMember Member(Guid membershipId, string name, bool isFormer = false, int joinedWeek = 1) =>
        new(membershipId, name, isFormer, joinedWeek);

    private static StandingsWeekResult Result(
        Guid membershipId,
        int week,
        int points,
        int correct = 0,
        int activeGames = 12,
        bool isWeekComplete = true) =>
        new(membershipId, week, points, correct, activeGames, isWeekComplete);

    private static GridCell Cell(GridCell[] cells, Guid gameSetGameId, Guid membershipId) =>
        cells.Single(cell => cell.GameSetGameId == gameSetGameId && cell.MembershipId == membershipId);
}
