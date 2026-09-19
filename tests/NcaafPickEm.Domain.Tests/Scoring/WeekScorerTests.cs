using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;
using static NcaafPickEm.Domain.Tests.Scoring.ScoringBuilder;

namespace NcaafPickEm.Domain.Tests.Scoring;

/// <summary>
/// Feature 06 acceptance criteria, one test per criterion, against
/// <c>04-Domain-Algorithms.md</c> section 7.
/// </summary>
public sealed class WeekScorerTests
{
    // --- Awarding points -----------------------------------------------------------------------

    [Fact]
    public void GivenAMemberPickedTheWinner_WhenScoring_ThenTheyEarnExactlyThatGamesLockedValue()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40)],
            members: [Member("Alyson")],
            picks: [Pick("Alyson", "Michigan vs Texas", "Michigan")]));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(40);
        score.CorrectCount.Should().Be(1);
    }

    [Fact]
    public void GivenAMemberPickedTheLoser_WhenScoring_ThenTheyEarnZeroForThatGame()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40)],
            members: [Member("Alyson")],
            picks: [Pick("Alyson", "Michigan vs Texas", "Texas")]));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(0);
        score.CorrectCount.Should().Be(0);
    }

    [Fact]
    public void GivenAMemberDidNotPick_WhenScoring_ThenTheyEarnZeroForThatGame()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40)],
            members: [Member("Alyson", SubmissionStatus.Incomplete)],
            picks: []));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(0);
        score.CorrectCount.Should().Be(0);
        score.ActiveGameCount.Should().Be(1, "a game nobody picked is still in the week");
    }

    /// <remarks>
    /// The weekly total is the sum over the set, and the point value is per game - so a member who
    /// is right about the 40-point game and wrong about the 5-point one is not on 45.
    /// </remarks>
    [Fact]
    public void GivenSeveralGames_WhenScoring_ThenTheWeeklyTotalIsTheSumOfTheOnesTheyGotRight()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40),
                Final("Georgia vs Kentucky", home: "Georgia", away: "Kentucky", 45, 13, points: 25),
                Final("TCU vs Baylor", home: "TCU", away: "Baylor", 21, 28, points: 5),
            ],
            members: [Member("Alyson")],
            picks:
            [
                Pick("Alyson", "Michigan vs Texas", "Michigan"),
                Pick("Alyson", "Georgia vs Kentucky", "Georgia"),
                Pick("Alyson", "TCU vs Baylor", "TCU"),
            ]));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(65);
        score.CorrectCount.Should().Be(2);
        score.ActiveGameCount.Should().Be(3);
    }

    [Fact]
    public void GivenAGameThatIsNotFinal_WhenScoring_ThenNobodyIsAwardedAnythingForIt()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                // Leading 21-0 in the fourth is not a result: section 7 scores on Final, not on
                // the scoreboard looking decided.
                Game(
                    "Ohio State vs Wisconsin",
                    home: "Ohio State",
                    away: "Wisconsin",
                    points: 30,
                    status: GameStatus.InProgress,
                    homeScore: 21,
                    awayScore: 0),
            ],
            members: [Member("Alyson")],
            picks: [Pick("Alyson", "Ohio State vs Wisconsin", "Ohio State")]));

        result.ScoreFor("Alyson").Points.Should().Be(0);
        result.IsWeekComplete.Should().BeFalse();
        result.NeedsReviewGameSetGameIds.Should().BeEmpty("a game still being played needs no decision");
    }

    // --- Ties and games with no winner ---------------------------------------------------------

    /// <remarks>
    /// The fixture week's Iowa State 24 - Kansas 24 (snapshot 5 onwards): a Final with no winner
    /// is a commissioner's problem, not a loss for everybody.
    /// </remarks>
    [Fact]
    public void GivenAFinalTie_WhenScoring_ThenItAwardsNothingAndIsFlaggedForReview()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Iowa State vs Kansas", home: "Iowa State", away: "Kansas", 24, 24, points: 15)],
            members: [Member("Alyson"), Member("Dance")],
            picks:
            [
                Pick("Alyson", "Iowa State vs Kansas", "Iowa State"),
                Pick("Dance", "Iowa State vs Kansas", "Kansas"),
            ]));

        result.ScoreFor("Alyson").Points.Should().Be(0);
        result.ScoreFor("Dance").Points.Should().Be(0);
        result.NeedsReviewGameSetGameIds.Should().Equal(GameSetGameId("Iowa State vs Kansas"));
        result.IsWeekComplete.Should().BeFalse("a game needing review holds the week open");
    }

    [Fact]
    public void GivenAFinalGameMissingAScore_WhenScoring_ThenItAwardsNothingAndIsFlaggedForReview()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Game(
                    "Oregon vs Washington",
                    home: "Oregon",
                    away: "Washington",
                    status: GameStatus.Final,
                    homeScore: 33,
                    awayScore: null),
            ],
            members: [Member("Alyson")],
            picks: [Pick("Alyson", "Oregon vs Washington", "Oregon")]));

        result.ScoreFor("Alyson").Points.Should().Be(0);
        result.NeedsReviewGameSetGameIds.Should().Equal(GameSetGameId("Oregon vs Washington"));
        result.IsWeekComplete.Should().BeFalse();
    }

    [Fact]
    public void GivenATieAnOverrideHasSettled_WhenScoring_ThenItScoresAndLeavesTheReviewList()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Game(
                    "Iowa State vs Kansas",
                    home: "Iowa State",
                    away: "Kansas",
                    points: 15,
                    status: GameStatus.Final,
                    homeScore: 24,
                    awayScore: 24,
                    overrideWinner: "Kansas"),
            ],
            members: [Member("Alyson"), Member("Dance")],
            picks:
            [
                Pick("Alyson", "Iowa State vs Kansas", "Iowa State"),
                Pick("Dance", "Iowa State vs Kansas", "Kansas"),
            ]));

        result.ScoreFor("Alyson").Points.Should().Be(0);
        result.ScoreFor("Dance").Points.Should().Be(15);
        result.NeedsReviewGameSetGameIds.Should().BeEmpty();
        result.IsWeekComplete.Should().BeTrue();
    }

    // --- Voided and removed games --------------------------------------------------------------

    [Fact]
    public void GivenAVoidedGame_WhenScoring_ThenItAwardsNothingEvenToWhoeverPickedTheWinner()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40),
                Game(
                    "Clemson vs Florida State",
                    home: "Clemson",
                    away: "Florida State",
                    points: 20,
                    status: GameStatus.Final,
                    homeScore: 28,
                    awayScore: 24,
                    isVoided: true),
            ],
            members: [Member("Alyson")],
            picks:
            [
                Pick("Alyson", "Michigan vs Texas", "Michigan"),
                Pick("Alyson", "Clemson vs Florida State", "Clemson"),
            ]));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(40, "the voided game contributes nothing");
        score.CorrectCount.Should().Be(1);
    }

    [Fact]
    public void GivenAVoidedGame_WhenScoring_ThenItIsExcludedFromActiveGameCount()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24),
                Game("Clemson vs Florida State", home: "Clemson", away: "Florida State", isVoided: true),
            ],
            members: [Member("Alyson")],
            picks: []));

        result.ScoreFor("Alyson").ActiveGameCount.Should().Be(1);
    }

    /// <remarks>
    /// Voiding is exactly how a cancelled or unplayable game stops holding the week open - the
    /// alternative would be a week that can never complete.
    /// </remarks>
    [Fact]
    public void GivenAGameThatWillNeverBePlayed_WhenItIsVoided_ThenTheWeekCanStillComplete()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24),
                Game("USC vs Stanford", home: "USC", away: "Stanford", status: GameStatus.Cancelled, isVoided: true),
            ],
            members: [Member("Alyson")],
            picks: []));

        result.IsWeekComplete.Should().BeTrue();
    }

    [Fact]
    public void GivenARowRemovedBeforeLock_WhenScoring_ThenItIsIgnoredEntirely()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40),
                Game(
                    "Penn State vs Youngstown State",
                    home: "Penn State",
                    away: "Youngstown State",
                    points: 20,
                    status: GameStatus.Final,
                    homeScore: 45,
                    awayScore: 3,
                    isRemoved: true),
            ],
            members: [Member("Alyson")],
            picks:
            [
                Pick("Alyson", "Michigan vs Texas", "Michigan"),
                Pick("Alyson", "Penn State vs Youngstown State", "Penn State"),
            ]));

        MemberWeekScore score = result.ScoreFor("Alyson");
        score.Points.Should().Be(40);
        score.ActiveGameCount.Should().Be(1);
    }

    // --- Who gets a row ------------------------------------------------------------------------

    [Fact]
    public void GivenAnIncompleteMemberWhoPickedSomeGames_WhenScoring_ThenTheirCorrectPicksStillCount()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40),
                Final("Georgia vs Kentucky", home: "Georgia", away: "Kentucky", 45, 13, points: 25),
            ],
            members: [Member("Alex", SubmissionStatus.Incomplete)],
            picks: [Pick("Alex", "Michigan vs Texas", "Michigan")]));

        MemberWeekScore score = result.ScoreFor("Alex");
        score.Points.Should().Be(40, "never pressing Submit does not forfeit the picks that were made");
        score.CorrectCount.Should().Be(1);
        score.ActiveGameCount.Should().Be(2);
    }

    /// <remarks>
    /// D-111: a member removed after lock keeps the <c>WeekSubmissions</c> row the lock job wrote,
    /// so the week they actually played still scores. Feature 07 shows them in that week's
    /// leaderboard and leaves them out of the season standings.
    /// </remarks>
    [Fact]
    public void GivenAFormerMemberWhoHeldALockRow_WhenScoring_ThenTheyStillGetAResult()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40)],
            members: [Member("Daniel")],
            picks: [Pick("Daniel", "Michigan vs Texas", "Michigan")]));

        result.ScoreFor("Daniel").Points.Should().Be(40);
    }

    [Theory]
    [InlineData(SubmissionStatus.NotStarted)]
    [InlineData(SubmissionStatus.InProgress)]
    [InlineData(SubmissionStatus.Submitted)]
    public void GivenARowTheLockJobNeverSettled_WhenScoring_ThenThatMembershipIsNotScored(SubmissionStatus status)
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24)],
            members: [Member("Alyson"), Member("Latecomer", status)],
            picks: [Pick("Latecomer", "Michigan vs Texas", "Michigan")]));

        result.Members.Should().ContainSingle()
            .Which.MembershipId.Should().Be(MemberId("Alyson"));
    }

    [Fact]
    public void GivenAPickFromSomebodyWithNoRow_WhenScoring_ThenItIsIgnored()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games: [Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24)],
            members: [Member("Alyson")],
            picks: [Pick("Stranger", "Michigan vs Texas", "Michigan")]));

        result.Members.Should().ContainSingle();
        result.ScoreFor("Alyson").Points.Should().Be(0);
    }

    // --- The week's Complete flag --------------------------------------------------------------

    [Fact]
    public void GivenEveryActiveGameFinalWithAWinner_WhenScoring_ThenTheWeekIsComplete()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24),
                Final("Georgia vs Kentucky", home: "Georgia", away: "Kentucky", 45, 13),
            ],
            members: [Member("Alyson")],
            picks: []));

        result.IsWeekComplete.Should().BeTrue();
    }

    [Fact]
    public void GivenOneGameStillToPlay_WhenScoring_ThenTheWeekIsNotComplete()
    {
        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24),
                Scheduled("San José State vs Hawai'i", home: "San José State", away: "Hawai'i"),
            ],
            members: [Member("Alyson")],
            picks: []));

        result.IsWeekComplete.Should().BeFalse();
    }

    // --- Idempotence ---------------------------------------------------------------------------

    /// <remarks>
    /// Feature 06: "points are only awarded once per game per member; re-processing a final result
    /// is idempotent." A full recompute from source rows makes that true by construction, and this
    /// is the test that says so.
    /// </remarks>
    [Fact]
    public void GivenTheSameWeek_WhenScoredTwice_ThenBothRunsAgreeExactly()
    {
        WeekScoringRequest request = Request(
            games:
            [
                Final("Michigan vs Texas", home: "Michigan", away: "Texas", 27, 24, points: 40),
                Final("Iowa State vs Kansas", home: "Iowa State", away: "Kansas", 24, 24, points: 15),
                Game("Clemson vs Florida State", home: "Clemson", away: "Florida State", isVoided: true),
            ],
            members: [Member("Alyson"), Member("Dance", SubmissionStatus.Incomplete)],
            picks:
            [
                Pick("Alyson", "Michigan vs Texas", "Michigan"),
                Pick("Alyson", "Iowa State vs Kansas", "Iowa State"),
                Pick("Dance", "Michigan vs Texas", "Texas"),
            ]);

        WeekScoreResult first = WeekScorer.Score(request);
        WeekScoreResult second = WeekScorer.Score(request);

        second.Should().BeEquivalentTo(first);
    }

    // --- The post-midnight finish, from the fixture itself --------------------------------------

    /// <summary>
    /// Feature 06: "a Saturday game that is delayed and finishes after midnight Eastern still
    /// counts for that week and is scored normally when Final." The fixture's San José State /
    /// Hawai'i kicks at 22:30 ET and is Final only in snapshot 6, whose own timestamp is 01:45 ET
    /// on the Sunday - so the scores asserted here are read out of that snapshot rather than
    /// retyped.
    /// </summary>
    [Fact]
    public async Task GivenTheFixturesPostMidnightFinish_WhenScoring_ThenItCountsLikeAnyOtherFinal()
    {
        var snapshotState = new FixtureSnapshotState();
        snapshotState.Set(FixtureSnapshotState.MaxSnapshot);

        IReadOnlyList<LiveScoreUpdate> updates = await new FixtureLiveScoreProvider(snapshotState)
            .GetScoresAsync(new DateOnly(2026, 10, 17), CancellationToken.None);

        LiveScoreUpdate lateGame = updates.Single(update => update.HomeName == "San José State");
        lateGame.Status.Should().Be(GameStatus.Final, "snapshot 6 is the one that finishes it");
        lateGame.KickoffUtc.Should().Be(new DateTime(2026, 10, 18, 2, 30, 0, DateTimeKind.Utc));

        WeekScoreResult result = WeekScorer.Score(Request(
            games:
            [
                Game(
                    "San José State vs Hawai'i",
                    home: lateGame.HomeName,
                    away: lateGame.AwayName,
                    points: 35,
                    status: lateGame.Status,
                    homeScore: lateGame.HomeScore,
                    awayScore: lateGame.AwayScore),
            ],
            members: [Member("Alyson"), Member("Dance")],
            picks:
            [
                Pick("Alyson", "San José State vs Hawai'i", lateGame.HomeName),
                Pick("Dance", "San José State vs Hawai'i", lateGame.AwayName),
            ]));

        result.ScoreFor("Alyson").Points.Should().Be(35);
        result.ScoreFor("Dance").Points.Should().Be(0);
        result.IsWeekComplete.Should().BeTrue();
    }
}
