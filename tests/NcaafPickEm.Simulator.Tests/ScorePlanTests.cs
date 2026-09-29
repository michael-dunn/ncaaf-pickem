using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Engine;

namespace NcaafPickEm.Simulator.Tests;

/// <summary><see cref="ScorePlan"/>: autoplay's scripts are plausible, deterministic, and never go backwards.</summary>
public sealed class ScorePlanTests
{
    private static readonly Guid GameId = Guid.Parse("0192a000-0000-7000-8000-000000000042");

    [Fact]
    public void GivenAGame_WhenSeeded_ThenTheSeedIsStableAcrossCalls()
    {
        ScorePlan.SeedFor(GameId).Should().Be(ScorePlan.SeedFor(Guid.Parse(GameId.ToString())));
    }

    [Fact]
    public void GivenManyGames_WhenTargetsAreDrawn_ThenNoneIsATieOrAnImpossibleScore()
    {
        for (int index = 0; index < 500; index++)
        {
            (int home, int away) = ScorePlan.DefaultTarget(index * 7919, spread: index % 3 == 0 ? null : -7.5m);

            home.Should().NotBe(away);
            home.Should().BeGreaterThanOrEqualTo(0).And.NotBe(1);
            away.Should().BeGreaterThanOrEqualTo(0).And.NotBe(1);
        }
    }

    [Fact]
    public void GivenABigHomeFavourite_WhenManyTargetsAreDrawn_ThenTheHomeTeamUsuallyWins()
    {
        int homeWins = Enumerable.Range(0, 400)
            .Select(index => ScorePlan.DefaultTarget(index * 104729, -21m))
            .Count(target => target.Home > target.Away);

        homeWins.Should().BeGreaterThan(300);
    }

    [Fact]
    public void GivenAPlan_WhenPlayedFromKickoffToTheEnd_ThenScoresOnlyRiseAndFinishOnTheTarget()
    {
        var plan = new GamePlan(31, 24);
        int seed = ScorePlan.SeedFor(GameId);
        (int home, int away) = (0, 0);

        for (double progress = 0; progress < 1; progress += 0.01)
        {
            PlannedState state = ScorePlan.At(seed, plan, progress);
            state.Status.Should().Be(GameStatus.InProgress);
            state.Home.Should().BeGreaterThanOrEqualTo(home);
            state.Away.Should().BeGreaterThanOrEqualTo(away);
            (home, away) = (state.Home!.Value, state.Away!.Value);
        }

        ScorePlan.At(seed, plan, 1).Should().Be(new PlannedState(GameStatus.Final, 31, 24, null, null));
        ScorePlan.At(seed, plan, -0.1).Status.Should().Be(GameStatus.Scheduled);
    }

    [Fact]
    public void GivenAMidGameRetarget_WhenPlayedOn_ThenItContinuesFromTheCurrentScore()
    {
        GamePlan plan = new GamePlan(10, 3).AnchoredAt(0.5, 14, 7);

        plan.TargetHome.Should().Be(14, "a target below the current score is raised to it");
        plan.TargetAway.Should().Be(7);

        PlannedState justAfter = ScorePlan.At(ScorePlan.SeedFor(GameId), plan with { TargetHome = 28 }, 0.5001);
        justAfter.Home.Should().BeGreaterThanOrEqualTo(14);
        justAfter.Away.Should().Be(7);
    }

    [Theory]
    [InlineData(0.0, 1, "15:00")]
    [InlineData(0.125, 1, "7:30")]
    [InlineData(0.5, 3, "15:00")]
    [InlineData(0.99, 4, "0:36")]
    public void GivenProgress_WhenTheClockIsRead_ThenQuarterAndTimeMatch(double progress, int quarter, string clock)
    {
        ScorePlan.Clock(progress).Should().Be(((byte)quarter, clock));
    }
}
