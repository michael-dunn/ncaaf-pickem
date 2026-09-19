using NcaafPickEm.Domain.Dashboard;
using NcaafPickEm.Shared.Enums;
using static NcaafPickEm.Domain.Tests.Dashboard.InfluenceBuilder;

namespace NcaafPickEm.Domain.Tests.Dashboard;

/// <summary>
/// Feature 05 acceptance criteria, one test per criterion, against
/// <c>04-Domain-Algorithms.md</c> section 6 - starting with the Overview worked example, asserted
/// from the fixture that holds it rather than retyped.
/// </summary>
public sealed class InfluenceCalculatorTests
{
    // --- The Overview worked example -----------------------------------------------------------

    [Theory]
    [InlineData("Dance")]
    [InlineData("Alyson")]
    public void GivenTheWorkedExample_WhenBuildingTheDashboard_ThenItMatchesTheDocumentedOne(string viewer)
    {
        InfluenceResult result = InfluenceCalculator.Calculate(InfluenceExample.RequestFor(viewer));

        IReadOnlyList<InfluenceExampleDashboardGame> expected = InfluenceExample.ExpectedFor(viewer);

        result.EveryoneAgrees.Should().BeEmpty();
        result.Games.Should().HaveCount(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            InfluenceGameResult actual = result.Games[i];

            actual.GameSetGameId.Should().Be(GameSetGameId(expected[i].Label));
            actual.MyTeamId.Should().Be(TeamId(expected[i].MyTeam));
            Names(actual.OppositePicks).Should().Equal(expected[i].OppositePicks);
            actual.OppositeCount.Should().Be(expected[i].OppositePicks.Count);
        }
    }

    [Fact]
    public void GivenTheWorkedExample_WhenBuildingDancesDashboard_ThenTheMostOpposedGameIsFirst()
    {
        InfluenceResult result = InfluenceCalculator.Calculate(InfluenceExample.RequestFor("Dance"));

        result.Games.Select(game => game.OppositeCount).Should().Equal(4, 1);
    }

    // --- Opposite picks list -------------------------------------------------------------------

    [Fact]
    public void GivenTheViewerPicked_WhenBuildingTheDashboard_ThenTheyAreInNoneOfTheirOwnLists()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Bo"],
            games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas")],
            picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Ada", "Michigan vs Texas", "Texas"),
                Pick("Bo", "Michigan vs Texas", "Michigan"),
            ]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).Games.Single();

        Names(game.OppositePicks).Should().Equal("Ada");
        Names(game.HomePickers).Should().Equal("Bo");
        Names(game.AwayPickers).Should().Equal("Ada");
        game.NoPick.Should().BeEmpty();
        game.MyTeamId.Should().Be(TeamId("Michigan"));
    }

    [Fact]
    public void GivenAMemberWithNoPick_WhenBuildingTheDashboard_ThenTheyAreInNoPickAndNotInOppositePicks()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Silent"],
            games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas")],
            picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Ada", "Michigan vs Texas", "Texas"),
            ]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).Games.Single();

        Names(game.NoPick).Should().Equal("Silent");
        Names(game.OppositePicks).Should().Equal("Ada");
        game.OppositeCount.Should().Be(1);
    }

    [Fact]
    public void GivenTheViewerDidNotPick_WhenBuildingTheDashboard_ThenItStaysInGamesWithBothTeamsLists()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Bo", "Cy"],
            games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas")],
            picks:
            [
                Pick("Ada", "Michigan vs Texas", "Michigan"),
                Pick("Bo", "Michigan vs Texas", "Michigan"),
                Pick("Cy", "Michigan vs Texas", "Texas"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        result.EveryoneAgrees.Should().BeEmpty();
        InfluenceGameResult game = result.Games.Single();
        game.MyTeamId.Should().BeNull();
        game.MyOutcome.Should().Be(InfluenceOutcome.NoPick);
        game.OppositePicks.Should().BeEmpty();
        game.OppositeCount.Should().Be(0);
        Names(game.HomePickers).Should().Equal("Ada", "Bo");
        Names(game.AwayPickers).Should().Equal("Cy");
    }

    // --- Ordering ------------------------------------------------------------------------------

    [Fact]
    public void GivenGamesWithDifferentOpposition_WhenOrdering_ThenCountThenPointsThenKickoffDecide()
    {
        DateTime early = Noon;
        DateTime late = Noon.AddHours(3);

        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Bo", "Cy"],
            games:
            [
                Game("One opposite, 10 points, late", "Maryland", "Rutgers", points: 10, kickoff: late),
                Game("One opposite, 10 points, early", "Ohio State", "Purdue", points: 10, kickoff: early),
                Game("One opposite, 20 points", "Oregon", "Washington", points: 20, kickoff: late),
                Game("Three opposite, 10 points", "Michigan", "Texas", points: 10, kickoff: late),
            ],
            picks:
            [
                // Everybody is against the viewer on Michigan/Texas.
                Pick("Me", "Three opposite, 10 points", "Michigan"),
                Pick("Ada", "Three opposite, 10 points", "Texas"),
                Pick("Bo", "Three opposite, 10 points", "Texas"),
                Pick("Cy", "Three opposite, 10 points", "Texas"),

                // Exactly one member is against the viewer on each of the other three.
                Pick("Me", "One opposite, 20 points", "Oregon"),
                Pick("Ada", "One opposite, 20 points", "Washington"),
                Pick("Bo", "One opposite, 20 points", "Oregon"),
                Pick("Cy", "One opposite, 20 points", "Oregon"),

                Pick("Me", "One opposite, 10 points, early", "Ohio State"),
                Pick("Ada", "One opposite, 10 points, early", "Purdue"),
                Pick("Bo", "One opposite, 10 points, early", "Ohio State"),
                Pick("Cy", "One opposite, 10 points, early", "Ohio State"),

                Pick("Me", "One opposite, 10 points, late", "Maryland"),
                Pick("Ada", "One opposite, 10 points, late", "Rutgers"),
                Pick("Bo", "One opposite, 10 points, late", "Maryland"),
                Pick("Cy", "One opposite, 10 points, late", "Maryland"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        result.Games.Select(game => game.GameSetGameId).Should().Equal(
            GameSetGameId("Three opposite, 10 points"),
            GameSetGameId("One opposite, 20 points"),
            GameSetGameId("One opposite, 10 points, early"),
            GameSetGameId("One opposite, 10 points, late"));
    }

    [Fact]
    public void GivenTwoGamesAlikeInEveryOrderingKey_WhenOrdering_ThenTheGameSetGameIdBreaksTheTie()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada"],
            games:
            [
                Game("Michigan vs Texas", home: "Michigan", away: "Texas"),
                Game("Maryland vs Rutgers", home: "Maryland", away: "Rutgers"),
            ],
            picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Ada", "Michigan vs Texas", "Texas"),
                Pick("Me", "Maryland vs Rutgers", "Maryland"),
                Pick("Ada", "Maryland vs Rutgers", "Rutgers"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        Guid[] byId =
        [
            .. new[] { GameSetGameId("Michigan vs Texas"), GameSetGameId("Maryland vs Rutgers") }.Order(),
        ];
        result.Games.Select(game => game.GameSetGameId).Should().Equal(byId);
    }

    [Fact]
    public void GivenAGameEverybodyAgreesOn_WhenSplitting_ThenItLeavesGamesForEveryoneAgrees()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Bo"],
            games:
            [
                Game("Agreed", home: "Michigan", away: "Texas"),
                Game("Contested", home: "Maryland", away: "Rutgers"),
            ],
            picks:
            [
                Pick("Me", "Agreed", "Michigan"),
                Pick("Ada", "Agreed", "Michigan"),
                Pick("Bo", "Agreed", "Michigan"),
                Pick("Me", "Contested", "Maryland"),
                Pick("Ada", "Contested", "Rutgers"),
                Pick("Bo", "Contested", "Maryland"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        result.Games.Should().ContainSingle()
            .Which.GameSetGameId.Should().Be(GameSetGameId("Contested"));
        result.EveryoneAgrees.Should().ContainSingle()
            .Which.GameSetGameId.Should().Be(GameSetGameId("Agreed"));
    }

    [Fact]
    public void GivenOppositePicks_WhenBuildingTheDashboard_ThenSwingPointsArePointValueTimesTheCount()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada", "Bo", "Cy"],
            games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas", points: 20)],
            picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Ada", "Michigan vs Texas", "Texas"),
                Pick("Bo", "Michigan vs Texas", "Texas"),
                Pick("Cy", "Michigan vs Texas", "Texas"),
            ]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).Games.Single();

        game.OppositeCount.Should().Be(3);
        game.SwingPoints.Should().Be(60);
    }

    // --- Game status ---------------------------------------------------------------------------

    [Fact]
    public void GivenAFinalScore_WhenBuildingTheDashboard_ThenTheViewerIsMarkedWonOrLost()
    {
        IReadOnlyList<InfluenceGame> games =
        [
            Game("Michigan vs Texas", "Michigan", "Texas", status: GameStatus.Final, homeScore: 28, awayScore: 21),
        ];
        IReadOnlyList<InfluencePick> picks =
        [
            Pick("Me", "Michigan vs Texas", "Michigan"),
            Pick("Ada", "Michigan vs Texas", "Texas"),
        ];

        InfluenceGameResult mine = InfluenceCalculator
            .Calculate(Request("Me", ["Me", "Ada"], games, picks)).Games.Single();
        InfluenceGameResult theirs = InfluenceCalculator
            .Calculate(Request("Ada", ["Me", "Ada"], games, picks)).Games.Single();

        mine.MyOutcome.Should().Be(InfluenceOutcome.Won);
        mine.WinnerTeamId.Should().Be(TeamId("Michigan"));
        theirs.MyOutcome.Should().Be(InfluenceOutcome.Lost);
    }

    [Fact]
    public void GivenAResultOverride_WhenBuildingTheDashboard_ThenItBeatsTheFinalScore()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me"],
            games:
            [
                Game(
                    "Michigan vs Texas",
                    "Michigan",
                    "Texas",
                    status: GameStatus.Final,
                    homeScore: 28,
                    awayScore: 21,
                    overrideWinner: "Texas"),
            ],
            picks: [Pick("Me", "Michigan vs Texas", "Michigan")]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).EveryoneAgrees.Single();

        game.WinnerTeamId.Should().Be(TeamId("Texas"));
        game.MyOutcome.Should().Be(InfluenceOutcome.Lost);
    }

    [Fact]
    public void GivenAFinalTie_WhenBuildingTheDashboard_ThenThereIsNoWinnerAndTheGameReadsPending()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me"],
            games:
            [
                Game("Iowa State vs Kansas", "Iowa State", "Kansas", points: 10, status: GameStatus.Final, homeScore: 24, awayScore: 24),
            ],
            picks: [Pick("Me", "Iowa State vs Kansas", "Iowa State")]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        InfluenceGameResult game = result.EveryoneAgrees.Single();
        game.WinnerTeamId.Should().BeNull();
        game.MyOutcome.Should().Be(InfluenceOutcome.Pending);
        result.PointsSoFar.Should().Be(0);
        result.MaxRemaining.Should().Be(0);
    }

    [Fact]
    public void GivenAGameInProgress_WhenBuildingTheDashboard_ThenTheLeadingTeamIsNotYetAWinner()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me"],
            games:
            [
                Game("Michigan vs Texas", "Michigan", "Texas", status: GameStatus.InProgress, homeScore: 28, awayScore: 21),
            ],
            picks: [Pick("Me", "Michigan vs Texas", "Michigan")]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).EveryoneAgrees.Single();

        game.WinnerTeamId.Should().BeNull();
        game.MyOutcome.Should().Be(InfluenceOutcome.Pending);
    }

    // --- Summary header ------------------------------------------------------------------------

    [Fact]
    public void GivenAWeekPartlyPlayed_WhenBuildingTheHeader_ThenPointsSoFarAndMaxRemainingAreSummed()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada"],
            games:
            [
                Game("Won", "Michigan", "Texas", points: 15, status: GameStatus.Final, homeScore: 28, awayScore: 21),
                Game("Lost", "Maryland", "Rutgers", points: 10, status: GameStatus.Final, homeScore: 7, awayScore: 31),
                Game("Still playing", "Oregon", "Washington", points: 20, status: GameStatus.InProgress, homeScore: 3, awayScore: 0),
                Game("Not picked", "Ohio State", "Purdue", points: 25),
                Game("Voided", "Iowa State", "Kansas", points: 30, isVoided: true),
            ],
            picks:
            [
                Pick("Me", "Won", "Michigan"),
                Pick("Me", "Lost", "Maryland"),
                Pick("Me", "Still playing", "Oregon"),
                Pick("Me", "Voided", "Iowa State"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        result.PointsSoFar.Should().Be(15);
        result.MaxRemaining.Should().Be(20);
    }

    // --- Who is on the dashboard at all --------------------------------------------------------

    [Fact]
    public void GivenAFormerMemberWhoWasActiveAtLock_WhenBuildingTheDashboard_ThenTheyAreStillListed()
    {
        InfluenceRequest request = new(
            ViewerMembershipId: MemberId("Me"),
            Games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas")],
            MembersActiveAtLock: [Member("Me"), Member("Gone", isFormer: true)],
            Picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Gone", "Michigan vs Texas", "Texas"),
            ]);

        InfluenceGameResult game = InfluenceCalculator.Calculate(request).Games.Single();

        game.OppositeCount.Should().Be(1);
        game.OppositePicks.Single().DisplayName.Should().Be("Gone");
        game.OppositePicks.Single().IsFormer.Should().BeTrue();
    }

    [Fact]
    public void GivenAMemberWhoJoinedAfterLock_WhenBuildingTheDashboard_ThenTheirPickIsIgnoredEntirely()
    {
        InfluenceRequest request = new(
            ViewerMembershipId: MemberId("Me"),
            Games: [Game("Michigan vs Texas", home: "Michigan", away: "Texas")],
            // The service leaves the newcomer out of the list; the calculator never looks further.
            MembersActiveAtLock: [Member("Me"), Member("Ada")],
            Picks:
            [
                Pick("Me", "Michigan vs Texas", "Michigan"),
                Pick("Ada", "Michigan vs Texas", "Michigan"),
                Pick("Newcomer", "Michigan vs Texas", "Texas"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        InfluenceGameResult game = result.EveryoneAgrees.Single();
        game.OppositeCount.Should().Be(0);
        game.OppositePicks.Should().BeEmpty();
        game.NoPick.Should().BeEmpty();
        Names(game.HomePickers).Should().Equal("Ada");
        game.AwayPickers.Should().BeEmpty();
    }

    [Fact]
    public void GivenAVoidedGame_WhenBuildingTheDashboard_ThenItIsInNeitherList()
    {
        InfluenceRequest request = Request(
            viewer: "Me",
            members: ["Me", "Ada"],
            games:
            [
                Game("Voided", "Michigan", "Texas", isVoided: true),
                Game("Played", "Maryland", "Rutgers"),
            ],
            picks:
            [
                Pick("Me", "Voided", "Michigan"),
                Pick("Ada", "Voided", "Texas"),
                Pick("Me", "Played", "Maryland"),
                Pick("Ada", "Played", "Rutgers"),
            ]);

        InfluenceResult result = InfluenceCalculator.Calculate(request);

        result.Games.Should().ContainSingle()
            .Which.GameSetGameId.Should().Be(GameSetGameId("Played"));
        result.EveryoneAgrees.Should().BeEmpty();
    }
}
