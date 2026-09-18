using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NcaafPickEm.Shared.Contracts.Seasons;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /api/seasons/{year}/weeks</c> (Feature 13) over the fixture week source: the 2026
/// calendar the whole app develops against until P2-02 ingests the real CFBD calendar.
/// </summary>
public sealed class SeasonEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SeasonEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Jobs:Enabled", "false")
            .UseSetting("Providers:ReferenceData", "Fixture"));
    }

    [Fact]
    public async Task GivenTheFixtureSeason_WhenGettingItsWeeks_ThenWeekZeroThroughChampionshipWeekComeBack()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/seasons/2026/weeks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SeasonWeek[]? weeks = await response.Content.ReadFromJsonAsync<SeasonWeek[]>();
        weeks.Should().NotBeNull();
        weeks!.Select(w => w.Week).Should().BeInAscendingOrder().And.Equal(Enumerable.Range(0, 16));
        weeks.Single(w => w.Week == 14).IsRegularSeason.Should().BeTrue();
        weeks.Single(w => w.Week == 15).IsRegularSeason.Should().BeFalse("week 15 is championship week");
    }

    [Fact]
    public async Task GivenTheFixtureSeason_WhenGettingItsWeeks_ThenEachWindowRunsSundayToSaturdayEastern()
    {
        using HttpClient client = _factory.CreateClient();

        SeasonWeek[]? weeks = await client.GetFromJsonAsync<SeasonWeek[]>("/api/seasons/2026/weeks");

        weeks.Should().NotBeNull();

        // Week 1 is the week of Saturday 5 September 2026: Sunday 30 August 00:00 EDT (04:00 UTC)
        // through Saturday 5 September 23:59:59.999 EDT (6 September 03:59:59.999 UTC).
        SeasonWeek weekOne = weeks!.Single(w => w.Week == 1);
        weekOne.StartUtc.Should().Be(new DateTimeOffset(2026, 8, 30, 4, 0, 0, TimeSpan.Zero));
        weekOne.EndUtc.Should().Be(new DateTimeOffset(2026, 9, 6, 3, 59, 59, 999, TimeSpan.Zero));

        foreach (SeasonWeek[] pair in weeks.OrderBy(w => w.Week).Zip(
                     weeks.OrderBy(w => w.Week).Skip(1),
                     (earlier, later) => new[] { earlier, later }))
        {
            pair[1].StartUtc.Should().Be(
                pair[0].EndUtc.AddMilliseconds(1),
                "week {0} must start the instant week {1} ends",
                pair[1].Week,
                pair[0].Week);
        }
    }

    [Fact]
    public async Task GivenASeasonTheCalendarDoesNotKnow_WhenGettingItsWeeks_ThenItIsNotFound()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/seasons/1999/weeks");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
