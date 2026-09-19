using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Health;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Proves the composition root boots under <c>WebApplicationFactory&lt;Program&gt;</c> and that
/// readiness really talks to the migrated <see cref="SqlTestDatabase"/> behind it.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class HealthEndpointTests
{
    private readonly ApiTestFixture _fixture;

    public HealthEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task GivenRunningApi_WhenProbingHealth_ThenItReturnsOk(string route)
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be(HealthResponse.Ok);
    }

    [Fact]
    public async Task GivenTestDatabase_WhenReadinessProbes_ThenTheSchemaIsAlreadyMigrated()
    {
        // The readiness probe only proves the app can connect; this proves the connection it made
        // is to a database SqlTestDatabase migrated, so every later test can seed real rows.
        string[] applied = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Database.GetAppliedMigrationsAsync()).ToArray());

        // Every migration in the assembly, not just the first: phases add their own (P4-01 added
        // Phase4_01_GameSetGameAddedUtc, P7-01 Phase7_01_PushRetries), and a test database missing
        // any of them fails confusingly later.
        applied.Should().NotBeEmpty();
        applied[0].Should().EndWith("Phase0_02_InitialSchema");

        string[] pending = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Database.GetPendingMigrationsAsync()).ToArray());

        pending.Should().BeEmpty();

        _fixture.Database.DatabaseName.Should().StartWith("NcaafPickEm_Test_");
    }
}
