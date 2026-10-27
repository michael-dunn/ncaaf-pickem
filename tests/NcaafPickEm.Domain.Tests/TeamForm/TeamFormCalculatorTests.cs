using NcaafPickEm.Domain.TeamForm;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.TeamForm;

/// <summary>
/// <c>04-Domain-Algorithms.md</c> section 14 (Feature 17): the last three games before a kickoff,
/// newest first, told from the team's side.
/// </summary>
public sealed class TeamFormCalculatorTests
{
    private static readonly Guid BostonCollege = TestIds.Of("team:Boston College");
    private static readonly Guid Duke = TestIds.Of("team:Duke");
    private static readonly Guid Syracuse = TestIds.Of("team:Syracuse");
    private static readonly Guid Clemson = TestIds.Of("team:Clemson");
    private static readonly Guid Fordham = TestIds.Of("team:Fordham");

    private static readonly DateTime Kickoff = new(2026, 10, 24, 19, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void GivenFourEarlierGames_WhenTakingLastGames_ThenTheThreeNewestComeBackNewestFirst()
    {
        FormGame oneWeekAgo = Game(1, BostonCollege, Fordham, 38, 7);
        FormGame twoWeeksAgo = Game(2, Syracuse, BostonCollege, 21, 24);
        FormGame threeWeeksAgo = Game(3, BostonCollege, Clemson, 10, 31);
        FormGame fourWeeksAgo = Game(4, Duke, BostonCollege, 17, 17);

        FormEntry[] form = TeamFormCalculator.LastGames(
            BostonCollege, Kickoff, [twoWeeksAgo, fourWeeksAgo, oneWeekAgo, threeWeeksAgo]);

        form.Select(entry => entry.GameId).Should().Equal(oneWeekAgo.GameId, twoWeeksAgo.GameId, threeWeeksAgo.GameId);
    }

    [Fact]
    public void GivenGamesOnBothSides_WhenTakingLastGames_ThenEachIsToldFromTheTeamsSide()
    {
        FormGame home = Game(1, BostonCollege, Fordham, 38, 7);
        FormGame away = Game(2, Clemson, BostonCollege, 31, 10);
        FormGame tie = Game(3, Syracuse, BostonCollege, 20, 20);

        FormEntry[] form = TeamFormCalculator.LastGames(BostonCollege, Kickoff, [home, away, tie]);

        form.Should().Equal(
            new FormEntry(home.GameId, Fordham, IsHome: true, 38, 7, FormResult.Win, home.KickoffUtc),
            new FormEntry(away.GameId, Clemson, IsHome: false, 10, 31, FormResult.Loss, away.KickoffUtc),
            new FormEntry(tie.GameId, Syracuse, IsHome: false, 20, 20, FormResult.Tie, tie.KickoffUtc));
    }

    [Fact]
    public void GivenGamesAtAndAfterTheKickoff_WhenTakingLastGames_ThenOnlyEarlierGamesCount()
    {
        FormGame earlier = Game(1, BostonCollege, Fordham, 38, 7);
        FormGame thisGame = new(TestIds.Of("game:this"), Duke, BostonCollege, Kickoff, 14, 21);
        FormGame later = Game(-1, BostonCollege, Clemson, 3, 45);

        FormEntry[] form = TeamFormCalculator.LastGames(BostonCollege, Kickoff, [earlier, thisGame, later]);

        form.Select(entry => entry.GameId).Should().Equal(
            [earlier.GameId],
            "the tray shows the form a team took into the game, so the game itself and anything after it never count");
    }

    [Fact]
    public void GivenOtherTeamsGamesAndFewerThanThree_WhenTakingLastGames_ThenOnlyTheTeamsGamesComeBack()
    {
        FormGame mine = Game(1, BostonCollege, Fordham, 38, 7);
        FormGame theirs = Game(2, Duke, Clemson, 24, 28);

        FormEntry[] form = TeamFormCalculator.LastGames(BostonCollege, Kickoff, [mine, theirs]);

        form.Should().ContainSingle().Which.GameId.Should().Be(mine.GameId);
    }

    [Fact]
    public void GivenNoGames_WhenTakingLastGames_ThenTheResultIsEmpty() =>
        TeamFormCalculator.LastGames(BostonCollege, Kickoff, []).Should().BeEmpty();

    /// <summary>A game <paramref name="weeksAgo"/> weeks before <see cref="Kickoff"/> (negative: after it).</summary>
    private static FormGame Game(int weeksAgo, Guid home, Guid away, int homeScore, int awayScore) =>
        new(TestIds.Of($"game:{weeksAgo}:{home}:{away}"), home, away, Kickoff.AddDays(-7 * weeksAgo), homeScore, awayScore);
}
