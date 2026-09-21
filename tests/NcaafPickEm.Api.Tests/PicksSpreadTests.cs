using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The picks page carries each game's point spread (D-181): <c>GET .../picks/me</c> reports the
/// newest <c>GameLines</c> row for every game that has one, so a member can weigh the line while
/// choosing. The frozen-after-lock half lives in <see cref="LockWeekJobTests"/>, which owns the
/// movable clock a lock needs.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class PicksSpreadTests
{
    private readonly ApiTestFixture _fixture;

    public PicksSpreadTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAGameWithALine_WhenAMemberLoadsTheirPicks_ThenTheGameCarriesItsSpread()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        // The fixture's lines.json puts a -7.5 consensus line on Michigan/Texas (700001), the
        // ranked matchup every Top-25 scenario set contains.
        Guid michiganTexas = await FixtureGameData.GetGameIdAsync(_fixture.PinnedFactory, 700001);

        using HttpResponseMessage response = await member.GetAsync($"{scenario.PicksRoute}/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();

        MyPickGameDto game = body!.Games.Single(g => g.Game.GameId == michiganTexas);
        game.Game.Spread.Should().Be(-7.5m);
    }
}
