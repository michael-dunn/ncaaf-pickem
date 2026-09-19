using NcaafPickEm.Api.Tests.Infrastructure;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The authorization matrix (05-Conventions.md, Testing) for the picks endpoint group added by
/// P4-01: anonymous 401, non-member 404, member on the commissioner route 403, commissioner 2xx.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class PicksAuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public PicksAuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenThePicksGroup_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            $"/api/leagues/{AuthMatrix.LeagueIdPlaceholder}/weeks/{PickWeekScenario.Week}/picks/me",
            $"/api/leagues/{AuthMatrix.LeagueIdPlaceholder}/weeks/{PickWeekScenario.Week}/picks/status");
}
