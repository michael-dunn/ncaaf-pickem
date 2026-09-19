using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Picks;

/// <summary>
/// Submission status derivation (Feature 04, <c>04-Domain-Algorithms.md</c> section 4).
/// </summary>
public sealed class SubmissionStatusTests
{
    private static readonly DateTime Monday = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid GameA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GameB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GameC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void GivenNoPicks_WhenCalculating_ThenTheMemberHasNotStarted()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(Set(GameA, GameB), [], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.NotStarted, 0, 2));
    }

    [Fact]
    public void GivenAnEmptySet_WhenCalculating_ThenTheMemberHasNotStarted()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate([], [], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.NotStarted, 0, 0));
    }

    [Fact]
    public void GivenSomeButNotAllGamesPicked_WhenCalculating_ThenTheMemberIsInProgress()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(Set(GameA, GameB), [GameA], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.InProgress, 1, 2));
    }

    [Fact]
    public void GivenEveryGamePickedButSubmitNeverPressed_WhenCalculating_ThenTheMemberIsStillInProgress()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(
            Set(GameA, GameB), [GameA, GameB], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.InProgress, 2, 2));
    }

    [Fact]
    public void GivenEveryGamePickedAndSubmitPressed_WhenCalculating_ThenTheMemberIsSubmitted()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(
            Set(GameA, GameB), [GameA, GameB], Monday.AddHours(1));

        state.Should().Be(new SubmissionState(SubmissionStatus.Submitted, 2, 2));
    }

    [Fact]
    public void GivenASubmittedMember_WhenAPickChanges_ThenTheyStaySubmitted()
    {
        // Changing a pick rewrites the Picks row; nothing about the inputs here changes, which is
        // exactly why the status holds (section 4: "SubmittedUtc stays").
        SubmissionState state = SubmissionStatusCalculator.Calculate(
            Set(GameA, GameB), [GameB, GameA], Monday.AddHours(1));

        state.Status.Should().Be(SubmissionStatus.Submitted);
    }

    [Fact]
    public void GivenASubmittedMember_WhenAGameIsAdded_ThenTheStatusRevertsToInProgress()
    {
        ActiveSetGame[] games =
        [
            new(GameA, Monday),
            new(GameB, Monday),
            new(GameC, Monday.AddHours(6)),
        ];

        SubmissionState state = SubmissionStatusCalculator.Calculate(games, [GameA, GameB], Monday.AddHours(1));

        state.Should().Be(new SubmissionState(SubmissionStatus.InProgress, 2, 3));
    }

    [Fact]
    public void GivenAGameAddedAfterSubmit_WhenItIsAlsoPicked_ThenTheMemberIsStillInProgressUntilTheyResubmit()
    {
        ActiveSetGame[] games =
        [
            new(GameA, Monday),
            new(GameB, Monday),
            new(GameC, Monday.AddHours(6)),
        ];

        SubmissionState state = SubmissionStatusCalculator.Calculate(
            games, [GameA, GameB, GameC], Monday.AddHours(1));

        state.Should().Be(new SubmissionState(SubmissionStatus.InProgress, 3, 3));
    }

    [Fact]
    public void GivenAGameAddedAfterSubmit_WhenTheMemberSubmitsAgain_ThenTheyAreSubmitted()
    {
        ActiveSetGame[] games =
        [
            new(GameA, Monday),
            new(GameB, Monday),
            new(GameC, Monday.AddHours(6)),
        ];

        SubmissionState state = SubmissionStatusCalculator.Calculate(
            games, [GameA, GameB, GameC], Monday.AddHours(7));

        state.Should().Be(new SubmissionState(SubmissionStatus.Submitted, 3, 3));
    }

    [Fact]
    public void GivenSubmitAtTheSameInstantAsTheAdd_WhenCalculating_ThenTheMemberIsSubmitted()
    {
        ActiveSetGame[] games = [new(GameA, Monday), new(GameB, Monday)];

        SubmissionState state = SubmissionStatusCalculator.Calculate(games, [GameA, GameB], Monday);

        state.Status.Should().Be(SubmissionStatus.Submitted);
    }

    [Fact]
    public void GivenASubmittedMember_WhenAGameIsRemoved_ThenTheyStaySubmittedAndThePickIsIgnored()
    {
        // GameC has left the active list; the member's pick on it is still in the database and is
        // simply not counted.
        SubmissionState state = SubmissionStatusCalculator.Calculate(
            Set(GameA, GameB), [GameA, GameB, GameC], Monday.AddHours(1));

        state.Should().Be(new SubmissionState(SubmissionStatus.Submitted, 2, 2));
    }

    [Fact]
    public void GivenAPickOnlyOnARemovedGame_WhenCalculating_ThenTheMemberHasNotStarted()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(Set(GameA, GameB), [GameC], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.NotStarted, 0, 2));
    }

    [Fact]
    public void GivenAGameAddedAfterSubmitThatIsThenRemovedAgain_WhenCalculating_ThenTheMemberIsSubmittedAgain()
    {
        SubmissionState state = SubmissionStatusCalculator.Calculate(
            Set(GameA, GameB), [GameA, GameB], Monday.AddHours(1));

        state.Status.Should().Be(SubmissionStatus.Submitted);
    }

    [Fact]
    public void GivenDuplicateGameRows_WhenCalculating_ThenEachGameCountsOnce()
    {
        ActiveSetGame[] games = [new(GameA, Monday), new(GameA, Monday), new(GameB, Monday)];

        SubmissionState state = SubmissionStatusCalculator.Calculate(games, [GameA], submittedUtc: null);

        state.Should().Be(new SubmissionState(SubmissionStatus.InProgress, 1, 2));
    }

    [Fact]
    public void GivenNullInputs_WhenCalculating_ThenItThrows()
    {
        Action nullGames = () => SubmissionStatusCalculator.Calculate(null!, [], null);
        Action nullPicks = () => SubmissionStatusCalculator.Calculate([], null!, null);

        nullGames.Should().Throw<ArgumentNullException>();
        nullPicks.Should().Throw<ArgumentNullException>();
    }

    private static ActiveSetGame[] Set(params Guid[] gameSetGameIds) =>
        [.. gameSetGameIds.Select(id => new ActiveSetGame(id, Monday))];
}
