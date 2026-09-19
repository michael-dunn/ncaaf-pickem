using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /api/reference/conferences</c> and <c>GET /api/reference/teams?search=</c> (P3-03),
/// against the Week 7, 2026 fixture teams (which include a handful of FCS schools that must
/// never appear).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class ReferenceEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public ReferenceEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);
        User user = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db));
        return _fixture.Factory.CreateClientAs(user.Id);
    }

    [Fact]
    public async Task GivenTheFixtureData_WhenListingConferences_ThenOnlyFbsConferencesAreReturned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/reference/conferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ConferenceDto[]? conferences = await response.Content.ReadFromJsonAsync<ConferenceDto[]>();
        conferences.Should().NotBeEmpty();
        conferences.Should().Contain(c => c.Abbreviation == "SEC");
    }

    [Fact]
    public async Task GivenAnFcsTeamAmongTheFixtureTeams_WhenListingTeams_ThenItIsExcluded()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/reference/teams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TeamDto[]? teams = await response.Content.ReadFromJsonAsync<TeamDto[]>();
        teams.Should().NotBeEmpty();
        teams.Should().NotContain(t => t.School == "Youngstown State");
        teams.Should().Contain(t => t.School == "Michigan");
    }

    [Fact]
    public async Task GivenASearchTerm_WhenListingTeams_ThenOnlyMatchingSchoolsAreReturned()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/reference/teams?search=Michigan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TeamDto[]? teams = await response.Content.ReadFromJsonAsync<TeamDto[]>();
        teams.Should().OnlyContain(t => t.School.Contains("Michigan"));
    }

    [Fact]
    public async Task GivenAnAnonymousCaller_WhenListingConferences_ThenItIs401()
    {
        HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/reference/conferences");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
