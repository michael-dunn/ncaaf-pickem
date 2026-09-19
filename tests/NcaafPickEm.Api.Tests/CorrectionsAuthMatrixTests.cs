using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The authorization matrix (05-Conventions.md, Testing) for the corrections endpoint group
/// added by P5-02: <c>GET .../audit</c> is member-scoped; the two <c>POST</c>s are commissioner
/// only. Unlike a plain <c>AuthMatrix.RunAsync</c> call, the commissioner routes need a real
/// locked game to succeed against, so this test seeds one directly.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class CorrectionsAuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public CorrectionsAuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheAuditRoute_WhenCalledByEachRole_ThenTheMatrixHolds()
    {
        (Guid leagueId, Guid commissionerUserId, Guid memberUserId, Guid strangerUserId, _, _) =
            await CreateLockedScenarioAsync();

        string route = $"/api/leagues/{leagueId}/audit";

        (await GetAsync(route, null)).Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(route, strangerUserId)).Should().Be(HttpStatusCode.NotFound);
        (await GetAsync(route, memberUserId)).Should().Be(HttpStatusCode.OK);
        (await GetAsync(route, commissionerUserId)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenTheOverrideResultRoute_WhenCalledByEachRole_ThenTheMatrixHolds()
    {
        (Guid leagueId, Guid commissionerUserId, Guid memberUserId, Guid strangerUserId, Guid gameId, _) =
            await CreateLockedScenarioAsync();

        string route = $"/api/leagues/{leagueId}/weeks/{FixtureGameData.Week}/gameset/games/{gameId}/override-result";
        Guid homeTeamId = await _fixture.Factory.QueryDbAsync(db => db.Games
            .Where(g => g.Id == gameId)
            .Select(g => g.HomeTeamId)
            .SingleAsync());
        Func<HttpContent> body = () => JsonContent.Create(new OverrideResultRequest(homeTeamId, "Auth matrix check."));

        (await PostAsync(route, null, body)).Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(route, strangerUserId, body)).Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(route, memberUserId, body)).Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(route, commissionerUserId, body)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenTheVoidRoute_WhenCalledByEachRole_ThenTheMatrixHolds()
    {
        (Guid leagueId, Guid commissionerUserId, Guid memberUserId, Guid strangerUserId, Guid gameId, _) =
            await CreateLockedScenarioAsync();

        string route = $"/api/leagues/{leagueId}/weeks/{FixtureGameData.Week}/gameset/games/{gameId}/void";
        Func<HttpContent> body = () => JsonContent.Create(new VoidGameRequest("Auth matrix check."));

        (await PostAsync(route, null, body)).Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(route, strangerUserId, body)).Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(route, memberUserId, body)).Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(route, commissionerUserId, body)).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A fresh league with a commissioner, a plain member, and a stranger, plus one fixture
    /// Saturday-FBS game already locked (seeded directly, the same way
    /// <c>ScoredWeekScenario</c> does, so no score/snapshot state needs touching).
    /// </summary>
    private async Task<(Guid LeagueId, Guid CommissionerUserId, Guid MemberUserId, Guid StrangerUserId, Guid GameId, Guid WeekGameSetId)>
        CreateLockedScenarioAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005); // Ohio State / Wisconsin

        return await _fixture.Factory.QueryDbAsync(async database =>
        {
            User commissioner = await TestUsers.CreateUserAsync(database, "Corrections Commish");
            User member = await TestUsers.CreateUserAsync(database, "Corrections Member");
            User stranger = await TestUsers.CreateUserAsync(database, "Corrections Stranger");

            League league = await TestUsers.CreateLeagueAsync(database, commissioner);
            await TestUsers.CreateMembershipAsync(database, league, commissioner, MembershipRole.Commissioner);
            await TestUsers.CreateMembershipAsync(database, league, member);

            Game game = await database.Games.SingleAsync(g => g.Id == gameId);

            var set = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = FixtureGameData.Week,
                GeneratedUtc = game.KickoffUtc.AddDays(-3),
                LockAtUtc = game.KickoffUtc,
                LockedUtc = game.KickoffUtc,
            };
            database.WeekGameSets.Add(set);

            database.WeekGameSetGames.Add(new WeekGameSetGame
            {
                Id = Guid.CreateVersion7(),
                WeekGameSetId = set.Id,
                GameId = gameId,
                Source = GameSetGameSource.Rule,
                AddedUtc = set.GeneratedUtc,
                ResolvedPointValue = 10,
            });

            await database.SaveChangesAsync();

            return (league.Id, commissioner.Id, member.Id, stranger.Id, gameId, set.Id);
        });
    }

    private async Task<HttpStatusCode> GetAsync(string route, Guid? userId)
    {
        using HttpClient client = userId is Guid id ? _fixture.Factory.CreateClientAs(id) : _fixture.Factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(route);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> PostAsync(string route, Guid? userId, Func<HttpContent> body)
    {
        using HttpClient client = _fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = body(),
        };
        request.Headers.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);

        if (userId is Guid id)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, id.ToString());
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
