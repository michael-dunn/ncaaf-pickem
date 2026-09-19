using System.Text.Json;
using System.Text.Json.Serialization;
using CollegeFootballData.Models;
using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="CfbdMapping"/> against the real, live CFBD captures from
/// <c>tests/NcaafPickEm.Fixtures/Real/cfbd-*.json</c> (P2-01's spike), not synthetic data.
/// </summary>
/// <remarks>
/// The Kiota-generated models (<see cref="Team"/>, <see cref="Conference"/>, <see cref="Game"/>,
/// <see cref="GameLine"/>, <see cref="PollRank"/>) implement <c>IParsable</c> for Kiota's own
/// parse-node pipeline, not <c>System.Text.Json</c> attributes, and the referenced serialization
/// package (<c>Microsoft.Kiota.Serialization.Json</c>) exposes only serialize helpers, not a
/// simple deserialize-from-JSON one. Rather than hand-roll a Kiota <c>IParseNode</c> walk, these
/// tests read the real captures via <see cref="FixtureLoader.ReadRealText"/> (embedded under
/// <c>Real/</c>, D-051) and deserialize with plain <c>System.Text.Json</c>
/// (<c>PropertyNameCaseInsensitive</c>, since CFBD's wire names are camelCase and the generated
/// properties are PascalCase — e.g. <c>alternateNames</c> binds to <c>AlternateNames</c> — plus a
/// <see cref="JsonStringEnumConverter"/> for the one enum field read, <c>Conference.Classification</c>)
/// straight into those real model types, then runs the exact <see cref="CfbdMapping"/> methods
/// <see cref="CfbdReferenceDataProvider"/> uses in production. This is the "or through your own
/// JSON reading" escape hatch the card allows, documented here per its instruction.
/// </remarks>
public sealed class CfbdMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    [Fact]
    public void GivenTheRealTeamsFbsCapture_WhenMapped_ThenSchoolMascotAndAlternateNamesRoundTrip()
    {
        Team appState = ReadCapture<Team>("cfbd-teams-fbs.json")
            .Single(t => t.School == "App State");

        ProviderTeam? mapped = CfbdMapping.MapTeam(appState, conferenceIdsByName: new Dictionary<string, int>());

        mapped.Should().NotBeNull();
        mapped!.CfbdId.Should().Be(2026);
        mapped.School.Should().Be("App State");
        mapped.Mascot.Should().Be("Mountaineers");
        mapped.Abbreviation.Should().Be("APP");
        mapped.Classification.Should().Be(TeamClassification.Fbs);
        mapped.AlternateNames.Should().BeEquivalentTo("Appalachian State", "APP", "App State");
    }

    [Fact]
    public void GivenARealTeamWithAVenueLocation_WhenMapped_ThenLocationIsNeverUsedAsAName()
    {
        Team appState = ReadCapture<Team>("cfbd-teams-fbs.json")
            .Single(t => t.School == "App State");

        // CFBD's Team.location is the *stadium* ("Kidd Brewer Stadium"), not the school name the
        // way ESPN's team.location is — the name collision the spike calls out.
        appState.Location!.Name.Should().Be("Kidd Brewer Stadium");

        ProviderTeam mapped = CfbdMapping.MapTeam(appState, new Dictionary<string, int>())!;

        mapped.School.Should().Be("App State");
        mapped.School.Should().NotBe(appState.Location.Name);
        mapped.AlternateNames.Should().NotContain(appState.Location.Name);
    }

    [Fact]
    public void GivenTheRealGamesCapture_WhenMapped_ThenKickoffTbdNeutralSiteAndVenueRoundTrip()
    {
        Game wakeForest = ReadCapture<Game>("cfbd-games-2025-week3.json")
            .Single(g => g.HomeTeam == "Wake Forest");

        ProviderGame mapped = CfbdMapping.MapGame(wakeForest)!;

        mapped.KickoffUtc.Should().Be(new DateTime(2025, 9, 11, 23, 30, 0, DateTimeKind.Utc));
        mapped.StartTimeTbd.Should().BeFalse();
        mapped.NeutralSite.Should().BeFalse();
        mapped.IsConferenceGame.Should().BeTrue();
        mapped.Venue.Should().Be("Allegacy Federal Credit Union Stadium");
    }

    [Theory]
    [InlineData("fbs", TeamClassification.Fbs)]
    [InlineData("FBS", TeamClassification.Fbs)]
    [InlineData("fcs", TeamClassification.Fcs)]
    [InlineData("ii", TeamClassification.Other)]
    [InlineData("ii/iii", TeamClassification.Other)]
    [InlineData("iii", TeamClassification.Other)]
    [InlineData(null, TeamClassification.Other)]
    public void GivenACfbdClassificationString_WhenMapped_ThenItLandsOnTheRightEnumValue(
        string? raw,
        TeamClassification expected) =>
        CfbdMapping.MapClassification(raw).Should().Be(expected);

    [Fact]
    public void GivenTheRealConferencesCapture_WhenMapped_ThenNameIsTheShortFormNotShortName()
    {
        Conference acc = ReadCapture<Conference>("cfbd-conferences-2025.json")
            .Single(c => c.Abbreviation == "ACC");

        ProviderConference? mapped = CfbdMapping.MapConference(acc);

        mapped.Should().NotBeNull();
        mapped!.CfbdId.Should().Be(1);

        // CFBD's `name` ("ACC") is the short form that matches Team.conference and ESPN's
        // groups.shortName; `shortName` ("Atlantic Coast Conference") is the long form. Mapping
        // from `name` is what lets a Team's `conference` string join back to this row.
        mapped.Name.Should().Be("ACC");
        mapped.Classification.Should().Be(TeamClassification.Fbs);
    }

    [Fact]
    public void GivenTheRealGamesCapture_WhenMapped_ThenScoresAndIdsRoundTripForAnFbsFcsMatchup()
    {
        Game indianaGame = ReadCapture<Game>("cfbd-games-2025-week3.json")
            .Single(g => g.HomeTeam == "Indiana" && g.AwayTeam == "Indiana State");

        ProviderGame? mapped = CfbdMapping.MapGame(indianaGame);

        mapped.Should().NotBeNull();
        mapped!.CfbdGameId.Should().Be(401752836);
        mapped.HomeCfbdTeamId.Should().Be(84);
        mapped.AwayCfbdTeamId.Should().Be(282);
        mapped.Completed.Should().BeTrue();
        mapped.HomePoints.Should().Be(73);
        mapped.AwayPoints.Should().Be(0);
        mapped.IsConferenceGame.Should().BeFalse();
    }

    [Fact]
    public void GivenTheRealLinesCapture_WhenMapped_ThenTheSpreadSignIsHomeRelativeWithNoConversion()
    {
        JsonElement root = JsonDocument.Parse(ReadRawCapture("cfbd-lines-2025-week3.json")).RootElement;
        JsonElement lsuFlorida = root.GetProperty("data").EnumerateArray()
            .Single(g => g.GetProperty("homeTeam").GetString() == "LSU");

        long cfbdGameId = lsuFlorida.GetProperty("id").GetInt64();
        GameLine espnBetLine = lsuFlorida.GetProperty("lines").Deserialize<List<GameLine>>(JsonOptions)!
            .Single(l => l.Provider == "ESPN Bet");

        ProviderLine? mapped = CfbdMapping.MapLine(cfbdGameId, espnBetLine, DateTime.UtcNow);

        mapped.Should().NotBeNull();
        mapped!.CfbdGameId.Should().Be(401752699);

        // LSU is home and favored (formattedSpread: "LSU -5.5"); home-relative, negative = home
        // favored, exactly `02-Data-Model.md`'s convention — no sign flip in the mapper.
        mapped.Spread.Should().Be(-5.5m);
    }

    [Fact]
    public void GivenTheRealRankingsCapture_WhenMapped_ThenTheApTop25RanksJoinOnTeamId()
    {
        JsonElement root = JsonDocument.Parse(ReadRawCapture("cfbd-rankings-2025-week3.json")).RootElement;
        JsonElement pollWeek = root.GetProperty("data")[0];
        JsonElement apPoll = pollWeek.GetProperty("polls").EnumerateArray()
            .Single(p => p.GetProperty("poll").GetString() == "AP Top 25");

        PollRank ohioState = apPoll.GetProperty("ranks").Deserialize<List<PollRank>>(JsonOptions)!
            .Single(r => r.Rank == 1);

        ProviderRanking? mapped = CfbdMapping.MapApRank(2025, 3, ohioState);

        mapped.Should().NotBeNull();
        mapped!.Season.Should().Be(2025);
        mapped.Week.Should().Be(3);
        mapped.Poll.Should().Be("AP");
        mapped.Rank.Should().Be(1);
        mapped.CfbdTeamId.Should().Be(194); // Ohio State
    }

    private static string ReadRawCapture(string fileName) => FixtureLoader.ReadRealText(fileName);

    private static List<T> ReadCapture<T>(string fileName)
    {
        string json = ReadRawCapture(fileName);
        CaptureFile<T> file = JsonSerializer.Deserialize<CaptureFile<T>>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{fileName} deserialized to null.");
        return file.Data;
    }

    private sealed record CaptureFile<T>(List<T> Data);
}
