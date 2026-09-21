using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01: what the app looks like with <c>ASPNETCORE_ENVIRONMENT=Production</c> — the
/// Development-only routes are gone, and an unhandled exception comes back as ProblemDetails
/// with nothing about the exception in it.
/// </summary>
/// <remarks>
/// The host is real (Program.cs, the real pipeline, the run's database) with two overrides: the
/// test authentication handler, so a signed-in request needs no identity header, and a
/// <see cref="LeagueService"/> registration that throws, which is how a genuinely unhandled
/// exception is produced inside a real endpoint without adding a throwing route to the app.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class ProductionBehaviourTests : IAsyncLifetime
{
    /// <summary>Distinctive text that must never reach the client.</summary>
    private const string SecretDetail = "connection-string-and-internals-should-not-leak";

    private readonly ApiTestFixture _fixture;
    private ProductionFactory _app = null!;

    public ProductionBehaviourTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _app = new ProductionFactory(_fixture.Database.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void GivenProduction_WhenMappingEndpoints_ThenTheDevelopmentOnlyRoutesAreGone()
    {
        string[] patterns = [.. RouteFact.From(_app.Services).Select(route => route.Pattern)];

        patterns.Should().NotContain("auth/dev-login", "dev-login signs in as a fixture user with no tailnet identity");
        patterns.Should().NotContain(
            pattern => pattern.StartsWith("api/admin/fixture", StringComparison.Ordinal),
            "the fixture snapshot controls step live scores by hand");
        patterns.Should().NotContain("api/push/test", "the push test route is a Development affordance");

        // The real routes are still there, so the assertions above are not passing vacuously.
        patterns.Should().Contain("api/me");
        patterns.Should().Contain("health/ready");
    }

    [Fact]
    public async Task GivenProduction_WhenAnEndpointThrows_ThenProblemDetailsLeaksNothing()
    {
        Guid userId = await _fixture.Factory.QueryDbAsync(async database =>
            (await TestUsers.CreateUserAsync(database)).Id);

        using HttpClient client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        string body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(SecretDetail, "the exception message must not reach the client");
        body.Should().NotContain("InvalidOperationException", "the exception type must not reach the client");
        body.Should().NotContain("NcaafPickEm.", "no stack frame may reach the client");
        body.Should().NotContain("stackTrace", "UseExceptionHandler must not attach the stack trace");
        body.Should().Contain("\"status\":500");
    }

    /// <summary>The app booted as Production, with a deliberately broken <see cref="LeagueService"/>.</summary>
    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public ProductionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jobs:Enabled", "false");

            // Explicit in Production: AddInfrastructure only defaults to Fixture in
            // Development/Testing and throws otherwise.
            builder.UseSetting("Providers:ReferenceData", "Fixture");
            builder.UseSetting("Providers:LiveScores", "Fixture");
            builder.UseSetting("Seed:DemoLeague", "false");
            builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { });

                services.RemoveAll<LeagueService>();
                services.AddScoped<LeagueService>(
                    _ => throw new InvalidOperationException(SecretDetail));
            });
        }
    }
}
