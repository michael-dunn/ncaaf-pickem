using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Providers;

/// <summary>
/// <see cref="GameMatcher"/> (P2-03, 04-Domain-Algorithms.md section 9): stored provider id
/// first, then the normalized team pair, then the swapped pair; abbreviations only as a last
/// resort; FCS payload noise ignored rather than surfaced.
/// </summary>
public sealed class GameMatcherTests
{
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    [Fact]
    public void GivenSchoolNamesThatMatchExactly_WhenMatched_ThenTheGameIsFound()
    {
        Team texas = Fbs("Texas", "TEX");
        Team ohioState = Fbs("Ohio State", "OSU");
        Game game = ScheduledGame(texas, ohioState);

        GameMatchResult result = Matcher([texas, ohioState], [], [game])
            .Match(Update(home: "Texas", away: "Ohio State"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
        result.SidesSwapped.Should().BeFalse();
    }

    [Fact]
    public void GivenAnEspnDisplayNameSpelling_WhenAnAliasCoversIt_ThenTheGameIsFound()
    {
        Team monroe = Fbs("UL Monroe", "ULM");
        Team southernMiss = Fbs("Southern Miss", "USM");
        Game game = ScheduledGame(southernMiss, monroe);
        TeamAlias alias = Alias(monroe, "UL Monroe Warhawks");

        GameMatchResult result = Matcher([monroe, southernMiss], [alias], [game])
            .Match(Update(home: "Southern Miss", away: "UL Monroe Warhawks"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
    }

    [Fact]
    public void GivenAnAliasForAnotherProvider_WhenMatching_ThenItIsNotUsed()
    {
        Team monroe = Fbs("UL Monroe", "ULM");
        Team southernMiss = Fbs("Southern Miss", "USM");
        Game game = ScheduledGame(southernMiss, monroe);
        TeamAlias cfbdOnly = Alias(monroe, "UL Monroe Warhawks", ProviderSource.Cfbd);

        GameMatchResult result = Matcher([monroe, southernMiss], [cfbdOnly], [game])
            .Match(Update(home: "Southern Miss", away: "UL Monroe Warhawks"));

        result.Outcome.Should().Be(GameMatchOutcome.Unmatched);
    }

    [Fact]
    public void GivenDiacritics_WhenMatched_ThenTheyAreFolded()
    {
        // ESPN ships "San José State" with U+00E9 and "Hawai'i" with an ASCII apostrophe.
        Team sanJose = Fbs("San Jose State", "SJSU");
        Team hawaii = Fbs("Hawaii", "HAW");
        Game game = ScheduledGame(sanJose, hawaii);

        GameMatchResult result = Matcher([sanJose, hawaii], [], [game])
            .Match(Update(home: "San José State", away: "Hawai'i"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
    }

    [Fact]
    public void GivenANameNothingResolves_WhenTheAbbreviationIsExact_ThenItIsTheLastResort()
    {
        Team southernMiss = Fbs("Southern Miss", "USM");
        Team monroe = Fbs("UL Monroe", "ULM");
        Game game = ScheduledGame(southernMiss, monroe);

        GameMatchResult result = Matcher([southernMiss, monroe], [], [game])
            .Match(new LiveScoreUpdate(
                "900001",
                Kickoff,
                "Southern Mississippi Golden Eagles",
                "USM",
                null,
                null,
                "UL Monroe",
                "ULM",
                null,
                null,
                GameStatus.Scheduled,
                "STATUS_SCHEDULED",
                false,
                null,
                null,
                null));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
    }

    [Fact]
    public void GivenTwoSchoolsSharingAnAbbreviation_WhenOnlyTheAbbreviationFits_ThenNothingIsGuessed()
    {
        // ESPN's abbreviations are its own and collide (USA, USM, USF). A wrong abbreviation
        // match silently scores the wrong game, so an ambiguous one resolves to nothing.
        Team southAlabama = Fbs("South Alabama", "USA");
        Team southernMiss = Fbs("Southern Miss", "USA");
        Team monroe = Fbs("UL Monroe", "ULM");
        Game game = ScheduledGame(southAlabama, monroe);

        GameMatchResult result = Matcher([southAlabama, southernMiss, monroe], [], [game])
            .Match(Update(home: "Not A School We Know", away: "UL Monroe", homeAbbreviation: "USA"));

        result.Outcome.Should().Be(GameMatchOutcome.Unmatched);
    }

    [Fact]
    public void GivenANeutralSiteGameLabelledTheOtherWayRound_WhenMatched_ThenTheSidesAreReportedSwapped()
    {
        Team kansas = Fbs("Kansas", "KU");
        Team arizonaState = Fbs("Arizona State", "ASU");

        // Our schedule has Arizona State at home; ESPN calls Kansas the home team.
        Game game = ScheduledGame(arizonaState, kansas);

        GameMatchResult result = Matcher([kansas, arizonaState], [], [game])
            .Match(Update(home: "Kansas", away: "Arizona State"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
        result.SidesSwapped.Should().BeTrue();
    }

    [Fact]
    public void GivenAStoredEspnEventId_WhenMatched_ThenNamesAreNotConsultedAtAll()
    {
        Team texas = Fbs("Texas", "TEX");
        Team ohioState = Fbs("Ohio State", "OSU");
        Game game = ScheduledGame(texas, ohioState);
        game.EspnEventId = 401856682;

        GameMatchResult result = Matcher([texas, ohioState], [], [game])
            .Match(Update(home: "nonsense", away: "gibberish", eventId: "401856682"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedById);
        result.Game.Should().BeSameAs(game);
    }

    [Fact]
    public void GivenAnFcsOpponent_WhenMatched_ThenItIsIgnoredRatherThanSurfaced()
    {
        // groups=80 returns every game involving an FBS team, so Howard at Indiana is in the
        // payload every Saturday. Surfacing it would bury the real problems (D-012).
        Team indiana = Fbs("Indiana", "IU");
        Team howard = Team("Howard", "HOW", TeamClassification.Fcs);

        GameMatchResult result = Matcher([indiana, howard], [], [])
            .Match(Update(home: "Indiana", away: "Howard"));

        result.Outcome.Should().Be(GameMatchOutcome.Ignored);
    }

    [Fact]
    public void GivenAnFcsOpponentThatIsOnOurSchedule_WhenMatched_ThenItStillGetsItsScore()
    {
        // The FCS rule decides what to do with an event we could not place; it never stops a game
        // we actually hold from being updated.
        Team indiana = Fbs("Indiana", "IU");
        Team indianaState = Team("Indiana State", "INST", TeamClassification.Fcs);
        Game game = ScheduledGame(indiana, indianaState);

        GameMatchResult result = Matcher([indiana, indianaState], [], [game])
            .Match(Update(home: "Indiana", away: "Indiana State"));

        result.Outcome.Should().Be(GameMatchOutcome.MatchedByTeams);
        result.Game.Should().BeSameAs(game);
    }

    [Fact]
    public void GivenTwoNamesWeHaveNeverHeardOf_WhenMatched_ThenItIsIgnored()
    {
        Team indiana = Fbs("Indiana", "IU");

        GameMatchResult result = Matcher([indiana], [], [])
            .Match(Update(home: "Gardner-Webb", away: "Stonehill"));

        result.Outcome.Should().Be(GameMatchOutcome.Ignored);
    }

    [Fact]
    public void GivenTwoKnownFbsTeamsWithNoGameBetweenThem_WhenMatched_ThenItIsUnmatched()
    {
        Team texas = Fbs("Texas", "TEX");
        Team ohioState = Fbs("Ohio State", "OSU");
        Team kansas = Fbs("Kansas", "KU");
        Team monroe = Fbs("UL Monroe", "ULM");

        GameMatchResult result = Matcher([texas, ohioState, kansas, monroe], [], [ScheduledGame(kansas, monroe)])
            .Match(Update(home: "Texas", away: "Ohio State"));

        result.Outcome.Should().Be(GameMatchOutcome.Unmatched);
        result.Game.Should().BeNull();
    }

    [Fact]
    public void GivenTheSamePairScheduledTwiceInTheWindow_WhenMatched_ThenNothingIsGuessed()
    {
        Team texas = Fbs("Texas", "TEX");
        Team ohioState = Fbs("Ohio State", "OSU");

        GameMatchResult result = Matcher(
                [texas, ohioState],
                [],
                [ScheduledGame(texas, ohioState), ScheduledGame(ohioState, texas)])
            .Match(Update(home: "Texas", away: "Ohio State"));

        result.Outcome.Should().Be(GameMatchOutcome.Ambiguous);
        result.Game.Should().BeNull();
    }

    [Fact]
    public void GivenACfbdFallbackUpdate_WhenMatched_ThenItGoesByTheCfbdGameIdAlone()
    {
        Team texas = Fbs("Texas", "TEX");
        Team ohioState = Fbs("Ohio State", "OSU");
        Game game = ScheduledGame(texas, ohioState);
        game.CfbdGameId = 700001;

        GameMatcher matcher = Matcher([texas, ohioState], [], [game]);

        matcher.Match(CfbdUpdate("700001")).Should().Match<GameMatchResult>(
            r => r.Outcome == GameMatchOutcome.MatchedById && r.Game == game);

        // CFBD carries no names, so a game we do not hold is noise, not an unmatched row.
        matcher.Match(CfbdUpdate("999999")).Outcome.Should().Be(GameMatchOutcome.Ignored);
    }

    private static readonly DateTime Kickoff = new(2026, 10, 17, 19, 30, 0, DateTimeKind.Utc);

    private static GameMatcher Matcher(
        IReadOnlyList<Team> teams,
        IReadOnlyList<TeamAlias> aliases,
        IReadOnlyList<Game> candidates) =>
        new(TeamNameIndex.Build(teams, aliases, ProviderSource.Espn), candidates);

    private static Team Fbs(string school, string abbreviation) =>
        Team(school, abbreviation, TeamClassification.Fbs);

    private static Team Team(string school, string abbreviation, TeamClassification classification) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            School = school,
            Abbreviation = abbreviation,
            Classification = classification,
        };

    private static TeamAlias Alias(Team team, string alias, ProviderSource source = ProviderSource.Espn) =>
        new() { Id = Guid.CreateVersion7(), TeamId = team.Id, Source = source, Alias = alias };

    private static Game ScheduledGame(Team home, Team away) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CfbdGameId = Random.Shared.NextInt64(700_000, 800_000),
            SeasonYear = 2026,
            Week = 7,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            KickoffUtc = Kickoff,
            KickoffEasternDate = Saturday,
            IsSaturdayEastern = true,
            Status = GameStatus.Scheduled,
        };

    private static LiveScoreUpdate Update(
        string home,
        string away,
        string eventId = "401900000",
        string homeAbbreviation = "HOME",
        string awayAbbreviation = "AWAY") =>
        new(
            eventId,
            Kickoff,
            home,
            homeAbbreviation,
            null,
            null,
            away,
            awayAbbreviation,
            null,
            null,
            GameStatus.Scheduled,
            "STATUS_SCHEDULED",
            false,
            null,
            null,
            null);

    private static LiveScoreUpdate CfbdUpdate(string cfbdGameId) =>
        new(
            cfbdGameId,
            Kickoff,
            string.Empty,
            string.Empty,
            null,
            null,
            string.Empty,
            string.Empty,
            null,
            null,
            GameStatus.Scheduled,
            "scheduled",
            false,
            null,
            null,
            null,
            ProviderSource.Cfbd);
}
