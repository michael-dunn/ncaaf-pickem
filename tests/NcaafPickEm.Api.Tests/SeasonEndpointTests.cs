using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Seasons;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /api/seasons/{year}/weeks</c> (Feature 13) over the fixture week source: the 2026
/// calendar the whole app develops against until P2-02 ingests the real CFBD calendar.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class SeasonEndpointTests
{
    private readonly ApiTestFixture _fixture;

    public SeasonEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Season Reader"));
        return _fixture.Factory.CreateClientAs(user.Id);
    }

    [Fact]
    public async Task GivenAnonymous_WhenGettingSeasonWeeks_ThenItIsUnauthorized()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/seasons/2026/weeks");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenTheFixtureSeason_WhenGettingItsWeeks_ThenWeekZeroThroughChampionshipWeekComeBack()
    {
        using HttpClient client = await SignedInClientAsync();

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
        using HttpClient client = await SignedInClientAsync();

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
        using HttpClient client = await SignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/seasons/1999/weeks");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
