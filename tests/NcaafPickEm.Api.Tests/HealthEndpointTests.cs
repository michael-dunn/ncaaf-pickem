using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Data;
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

    [Fact]
    public async Task GivenStartupMigrationIsRunning_WhenReadinessProbes_ThenItIs503UntilItFinishes()
    {
        // P8-05: Kestrel is started by GenericWebHostService, which the web host registers before
        // AddInfrastructure registers the migrator, so the container answers HTTP while the schema
        // is still being applied. Docker's HEALTHCHECK and `depends_on: service_healthy` both read
        // this route, so "reachable" must not be reported as "ready".
        DatabaseStartupState state = _fixture.Factory.Services.GetRequiredService<DatabaseStartupState>();
        using HttpClient client = _fixture.Factory.CreateClient();

        state.BeginMigrating();
        try
        {
            using HttpResponseMessage migrating = await client.GetAsync("/health/ready");

            migrating.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            migrating.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        }
        finally
        {
            state.EndMigrating();
        }

        using HttpResponseMessage ready = await client.GetAsync("/health/ready");

        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
