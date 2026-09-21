using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01 / D-153: the fixed-window rate limits on <c>/auth/*</c> (since P9-03,
/// <c>/auth/dev-login</c> alone) and <c>/api/invites/*</c>.
/// </summary>
/// <remarks>
/// The shared <see cref="ApiFactory"/> turns the limiter off — every in-memory test request has
/// no remote IP, so the whole suite would share one partition — so this class boots its own host
/// with the limiter on and a window of three requests a minute, which keeps the test fast and
/// independent of the shipped defaults.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class RateLimitTests : IAsyncLifetime
{
    private const int Permit = 3;

    private readonly ApiTestFixture _fixture;
    private LimitedFactory _app = null!;

    public RateLimitTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _app = new LimitedFactory(_fixture.Database.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenTooManySignInAttempts_WhenTheWindowIsExceeded_ThenItIs429ProblemDetails()
    {
        using HttpClient client = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // /auth/dev-login without a ?user is a 400 from the endpoint itself, which is the
        // cheapest way to spend the window without seeding anything.
        for (int attempt = 0; attempt < Permit; attempt++)
        {
            using HttpResponseMessage allowed = await client.GetAsync("/auth/dev-login");
            allowed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "attempt {0} is inside the window", attempt + 1);
        }

        using HttpResponseMessage refused = await client.GetAsync("/auth/dev-login");

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        refused.Headers.Contains("Retry-After").Should().BeTrue("the client should be told when to come back");
    }

    [Fact]
    public async Task GivenTooManyInviteCodeGuesses_WhenTheWindowIsExceeded_ThenItIs429()
    {
        Guid userId = await _fixture.Factory.QueryDbAsync(async database =>
            (await TestUsers.CreateUserAsync(database)).Id);

        using HttpClient client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());

        for (int attempt = 0; attempt < Permit; attempt++)
        {
            using HttpResponseMessage allowed = await client.GetAsync($"/api/invites/GUESS{attempt:D3}");
            allowed.StatusCode.Should().Be(HttpStatusCode.NotFound, "an unknown code is 404, not a limit");
        }

        using HttpResponseMessage refused = await client.GetAsync("/api/invites/GUESSXXX");

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GivenTheLimitedRoutes_WhenAnUnlimitedRouteIsCalledRepeatedly_ThenItIsNeverRefused()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));

        using HttpClient client = _app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Id.ToString());

        // The two policies are partitioned per policy as well as per client, and no other route
        // carries one, so an ordinary API read is untouched however often it is called.
        for (int attempt = 0; attempt < Permit * 3; attempt++)
        {
            using HttpResponseMessage response = await client.GetAsync("/api/me");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>The app with the limiter on and a three-per-minute window.</summary>
    private sealed class LimitedFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public LimitedFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jobs:Enabled", "false");
            builder.UseSetting("Providers:ReferenceData", "Fixture");
            builder.UseSetting("Providers:LiveScores", "Fixture");
            builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");

            builder.UseSetting(RateLimitingSetup.EnabledKey, "true");
            builder.UseSetting("RateLimiting:AuthPermitPerMinute", Permit.ToString());
            builder.UseSetting("RateLimiting:InvitePermitPerMinute", Permit.ToString());

            builder.ConfigureTestServices(services =>
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { }));
        }
    }
}
