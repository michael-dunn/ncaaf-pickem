using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Scoring;

/// <summary>
/// The shared winner rule (<c>04-Domain-Algorithms.md</c> sections 6 and 7): override, else the
/// higher score once Final, else nobody.
/// </summary>
public sealed class WinnerResolverTests
{
    private static readonly Guid Home = TestIds.Of("team:Michigan");
    private static readonly Guid Away = TestIds.Of("team:Texas");
    private static readonly Guid Neither = TestIds.Of("team:Maryland");

    [Fact]
    public void GivenAFinalGame_WhenResolving_ThenTheHigherScoreWins()
    {
        Resolve(status: GameStatus.Final, homeScore: 28, awayScore: 21).Should().Be(Home);
        Resolve(status: GameStatus.Final, homeScore: 21, awayScore: 28).Should().Be(Away);
    }

    [Fact]
    public void GivenAFinalTie_WhenResolving_ThenThereIsNoWinner()
    {
        Resolve(status: GameStatus.Final, homeScore: 24, awayScore: 24).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 21)]
    [InlineData(28, null)]
    [InlineData(null, null)]
    public void GivenAFinalGameMissingAScore_WhenResolving_ThenThereIsNoWinner(int? home, int? away)
    {
        Resolve(status: GameStatus.Final, homeScore: home, awayScore: away).Should().BeNull();
    }

    [Theory]
    [InlineData(GameStatus.Scheduled)]
    [InlineData(GameStatus.InProgress)]
    [InlineData(GameStatus.Postponed)]
    [InlineData(GameStatus.Cancelled)]
    public void GivenAGameThatIsNotFinal_WhenResolving_ThenTheScoreIsNotRead(GameStatus status)
    {
        Resolve(status, homeScore: 28, awayScore: 21).Should().BeNull();
    }

    [Fact]
    public void GivenAnOverride_WhenResolving_ThenItBeatsTheFinalScore()
    {
        Resolve(status: GameStatus.Final, homeScore: 28, awayScore: 21, overrideWinner: Away)
            .Should().Be(Away);
    }

    [Fact]
    public void GivenAnOverride_WhenResolving_ThenItStandsEvenWithoutAFinalScore()
    {
        Resolve(status: GameStatus.Scheduled, homeScore: null, awayScore: null, overrideWinner: Home)
            .Should().Be(Home);
    }

    [Fact]
    public void GivenAnOverrideOnAFinalTie_WhenResolving_ThenTheOverriddenTeamWins()
    {
        Resolve(status: GameStatus.Final, homeScore: 24, awayScore: 24, overrideWinner: Away)
            .Should().Be(Away);
    }

    [Fact]
    public void GivenAnOverrideOnATeamNotPlaying_WhenResolving_ThenItIsReturnedUnchanged()
    {
        // The resolver reports what the commissioner recorded; validating the team is on the field
        // is the correction endpoint's job (P5-02), not this rule's.
        Resolve(status: GameStatus.Final, homeScore: 28, awayScore: 21, overrideWinner: Neither)
            .Should().Be(Neither);
    }

    [Theory]
    [InlineData(GameStatus.Final, 28, 21, true)]
    [InlineData(GameStatus.Final, 24, 24, false)]
    [InlineData(GameStatus.Final, null, null, false)]
    [InlineData(GameStatus.InProgress, 28, 21, false)]
    public void GivenAGame_WhenAskingWhetherAWinnerIsDeterminable_ThenItAgreesWithResolve(
        GameStatus status,
        int? homeScore,
        int? awayScore,
        bool expected)
    {
        WinnerResolver.HasDeterminableWinner(null, status, homeScore, awayScore).Should().Be(expected);
        (Resolve(status, homeScore, awayScore) is not null).Should().Be(expected);
    }

    [Fact]
    public void GivenAnOverride_WhenAskingWhetherAWinnerIsDeterminable_ThenItIs()
    {
        WinnerResolver.HasDeterminableWinner(Away, GameStatus.Scheduled, null, null).Should().BeTrue();
    }

    private static Guid? Resolve(
        GameStatus status,
        int? homeScore,
        int? awayScore,
        Guid? overrideWinner = null) =>
        WinnerResolver.Resolve(overrideWinner, status, homeScore, awayScore, Home, Away);
}
