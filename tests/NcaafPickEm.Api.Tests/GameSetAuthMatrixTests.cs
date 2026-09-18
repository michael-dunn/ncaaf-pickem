using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The authorization matrix (05-Conventions.md, Testing) for the game-set and point-rule
/// endpoint groups added by P3-03.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class GameSetAuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public GameSetAuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheGameSetGroup_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            "/api/leagues/{leagueId}/weeks/7/gameset",
            "/api/leagues/{leagueId}/gameset-rules",
            body: static () => JsonContent.Create(Array.Empty<GameSetRuleDto>()),
            commissionerMethod: HttpMethod.Put);

    [Fact]
    public async Task GivenThePointRulesGroup_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        // No member-only point-rules route exists; the game-set week GET stands in for the
        // member half, matching the pattern LeagueEndpointsTests uses for the Invites group.
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            "/api/leagues/{leagueId}/weeks/7/gameset",
            "/api/leagues/{leagueId}/point-rules",
            commissionerMethod: HttpMethod.Get);
}
