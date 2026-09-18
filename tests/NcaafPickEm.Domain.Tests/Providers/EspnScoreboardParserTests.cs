using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Providers.Espn;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Providers;

/// <summary>
/// <see cref="EspnScoreboardParser"/> (P2-03) against the real trimmed captures from the P2-01
/// spike: a finished Saturday (<c>espn-scoreboard-20260912.json</c>, 4 events, all Final) and an
/// upcoming one (<c>espn-scoreboard-20260919.json</c>, 4 events, all scheduled, all with odds).
/// </summary>
public sealed class EspnScoreboardParserTests
{
    private const string FinalSaturday = "espn-scoreboard-20260912.json";
    private const string ScheduledSaturday = "espn-scoreboard-20260919.json";

    private static IReadOnlyList<LiveScoreUpdate> Parse(string capture) =>
        EspnScoreboardParser.Parse(FixtureLoader.ReadRealText(capture));

    [Fact]
    public void GivenFinishedSaturdayCapture_WhenParsed_ThenEveryEventIsFinal()
    {
        IReadOnlyList<LiveScoreUpdate> updates = Parse(FinalSaturday);

        updates.Should().HaveCount(4);
        updates.Should().OnlyContain(u => u.Status == GameStatus.Final);
        updates.Should().OnlyContain(u => u.Completed);
        updates.Should().OnlyContain(u => u.RawStatusName == "STATUS_FINAL");
    }

    [Fact]
    public void GivenFinishedGame_WhenParsed_ThenNamesScoresAndIdsComeFromTheRightFields()
    {
        LiveScoreUpdate game = Parse(FinalSaturday).Single(u => u.SourceEventId == "401856682");

        // team.location, not displayName: the school name with no mascot is what CFBD's `school`
        // can be compared against.
        game.HomeName.Should().Be("Texas");
        game.AwayName.Should().Be("Ohio State");
        game.HomeAbbreviation.Should().Be("TEX");
        game.AwayAbbreviation.Should().Be("OSU");
        game.HomeSourceTeamId.Should().Be("251");
        game.AwaySourceTeamId.Should().Be("194");
        game.HomeScore.Should().Be(24);
        game.AwayScore.Should().Be(23);
        game.Source.Should().Be(ProviderSource.Espn);
    }

    [Fact]
    public void GivenMinutePrecisionKickoff_WhenParsed_ThenItRoundTripsAsUtc()
    {
        LiveScoreUpdate game = Parse(FinalSaturday).Single(u => u.SourceEventId == "401856682");

        // "2026-09-12T23:30Z" - no seconds, no offset digits. ParseExact would throw.
        game.KickoffUtc.Should().Be(new DateTime(2026, 9, 12, 23, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GivenFinalGame_WhenParsed_ThenPeriodAndClockAreNotCarried()
    {
        // ESPN reports period 4 and "0:00" on a finished game; a live clock on a final score is
        // noise, so only an in-progress game carries them.
        Parse(FinalSaturday).Should().OnlyContain(u => u.Period == null && u.Clock == null);
    }

    [Fact]
    public void GivenScheduledSaturdayCapture_WhenParsed_ThenNothingIsFinalAndNoScoreIsRead()
    {
        IReadOnlyList<LiveScoreUpdate> updates = Parse(ScheduledSaturday);

        updates.Should().HaveCount(4);
        updates.Should().OnlyContain(u => u.Status == GameStatus.Scheduled);
        updates.Should().OnlyContain(u => !u.Completed);

        // Every competitor reports "0" before kickoff. Reading that would fabricate a 0-0 tie.
        updates.Should().OnlyContain(u => u.HomeScore == null && u.AwayScore == null);
    }

    [Fact]
    public void GivenHomeFavourite_WhenParsed_ThenSpreadStaysNegativeWithNoConversion()
    {
        LiveScoreUpdate game = Parse(ScheduledSaturday).Single(u => u.SourceEventId == "401869940");

        game.HomeName.Should().Be("Delaware");
        game.Spread.Should().Be(-5.5m);
    }

    [Fact]
    public void GivenAwayFavourite_WhenParsed_ThenSpreadIsPositive()
    {
        LiveScoreUpdate game = Parse(ScheduledSaturday).Single(u => u.SourceEventId == "401856686");

        game.HomeName.Should().Be("Arkansas");
        game.AwayName.Should().Be("Georgia");
        game.Spread.Should().Be(24.5m);
    }

    [Fact]
    public void GivenNeutralSiteGame_WhenParsed_ThenEspnStillLabelsOneSideHome()
    {
        // Kansas is "home" against Arizona State at a neutral site; the matcher has to try the
        // swapped pair before calling it unmatched.
        LiveScoreUpdate game = Parse(ScheduledSaturday).Single(u => u.SourceEventId == "401856812");

        game.HomeName.Should().Be("Kansas");
        game.AwayName.Should().Be("Arizona State");
    }

    [Fact]
    public void GivenUnknownStatusName_WhenMapped_ThenTheStateFallbackDecides()
    {
        EspnStatusMapper.Map("STATUS_SOMETHING_NEW", "pre", false, out bool preRecognized)
            .Should().Be(GameStatus.Scheduled);
        preRecognized.Should().BeFalse();

        EspnStatusMapper.Map("STATUS_SOMETHING_NEW", "in", false, out _).Should().Be(GameStatus.InProgress);
        EspnStatusMapper.Map("STATUS_SOMETHING_NEW", "post", true, out _).Should().Be(GameStatus.Final);

        // "post" without `completed` is the ambiguous case: never Final on a guess.
        EspnStatusMapper.Map("STATUS_SOMETHING_NEW", "post", false, out _).Should().Be(GameStatus.InProgress);
        EspnStatusMapper.Map("STATUS_SOMETHING_NEW", "who knows", false, out _).Should().BeNull();
    }

    [Theory]
    [InlineData("STATUS_SCHEDULED", GameStatus.Scheduled)]
    [InlineData("STATUS_IN_PROGRESS", GameStatus.InProgress)]
    [InlineData("STATUS_HALFTIME", GameStatus.InProgress)]
    [InlineData("STATUS_END_PERIOD", GameStatus.InProgress)]
    [InlineData("STATUS_END_OF_PERIOD", GameStatus.InProgress)]
    [InlineData("STATUS_DELAYED", GameStatus.InProgress)]
    [InlineData("STATUS_FINAL", GameStatus.Final)]
    [InlineData("STATUS_POSTPONED", GameStatus.Postponed)]
    [InlineData("STATUS_CANCELED", GameStatus.Cancelled)]
    public void GivenKnownStatusName_WhenMapped_ThenItMatchesTheAlgorithmTable(string name, GameStatus expected)
    {
        GameStatus? mapped = EspnStatusMapper.Map(name, state: null, completed: false, out bool recognized);

        mapped.Should().Be(expected);
        recognized.Should().BeTrue();
    }

    [Fact]
    public void GivenAnEmptyPayload_WhenParsed_ThenNothingIsReturned()
    {
        EspnScoreboardParser.Parse("{}").Should().BeEmpty();
        EspnScoreboardParser.Parse("{\"events\":[]}").Should().BeEmpty();
    }
}
