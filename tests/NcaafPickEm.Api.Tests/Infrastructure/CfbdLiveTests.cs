using CollegeFootballData;
using CollegeFootballData.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Jobs.Refresh;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// One live, authenticated call against the real CollegeFootballData API — the only network call
/// this test suite ever makes. Verifies the client wiring (<c>CfbdReferenceDataProvider</c>'s
/// construction shape) actually reaches CFBD and gets real team data back, not just that it
/// compiles against the Kiota models.
/// </summary>
/// <remarks>
/// Opt-in only: it does nothing unless both <c>CFBD_LIVE=1</c> and <c>Cfbd__ApiKey</c> are set in
/// the same shell (<c>Implementation/AGENT-NOTES.md</c>, "Reference data ingest"). Run it alone
/// with <c>dotnet test --filter "FullyQualifiedName~CfbdLiveTests"</c>; the key is never logged,
/// printed, or committed. Tagged <c>Category=Live</c> so a filtered run
/// (<c>--filter "Category!=Live"</c>) skips it entirely without needing the opt-in check at all.
/// </remarks>
[Trait("Category", "Live")]
public sealed class CfbdLiveTests
{
    [Fact]
    public async Task GivenTheRealCfbdApiKey_WhenFetchingFbsTeams_ThenItReturnsRealTeams()
    {
        string? apiKey = Environment.GetEnvironmentVariable("Cfbd__ApiKey");
        bool liveEnabled = Environment.GetEnvironmentVariable("CFBD_LIVE") == "1";

        if (!liveEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var authProvider = new BaseBearerTokenAuthenticationProvider(new StaticAccessTokenProvider(apiKey));
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.collegefootballdata.com") };
        var requestAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        var client = new ApiClient(requestAdapter);

        List<Team>? teams = await client.Teams.Fbs.GetAsync(cfg => cfg.QueryParameters.Year = DateTime.UtcNow.Year - 1);

        teams.Should().NotBeNull();
        teams!.Should().NotBeEmpty();
        teams.Should().Contain(t => t.Classification == "fbs");
    }

    /// <summary>
    /// P8-07: the whole first-start bootstrap against the real API and a real (throwaway)
    /// database — calendar, conferences + teams + aliases, then the current week's schedule,
    /// rankings and lines. This is the test that would have caught the 2026 calendar collision
    /// and the duplicate-alias insert before they reached the deployment; unit tests can only
    /// assert the shapes we thought to write down.
    /// </summary>
    [Fact]
    public async Task GivenTheRealCfbdApi_WhenBootstrappingTheCurrentSeason_ThenEveryStepSucceeds()
    {
        string? apiKey = Environment.GetEnvironmentVariable("Cfbd__ApiKey");
        bool liveEnabled = Environment.GetEnvironmentVariable("CFBD_LIVE") == "1";

        if (!liveEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        await using SqlTestDatabase database = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(
            database.ConnectionString,
            settings: new Dictionary<string, string>
            {
                ["Providers:ReferenceData"] = "Cfbd",
                [$"{NcaafPickEm.Infrastructure.Providers.Cfbd.CfbdOptions.SectionName}:ApiKey"] = apiKey,

                // Jobs stay off, so the bootstrap is switched on explicitly (the key a real
                // deployment never needs to set) and nothing else calls CFBD.
                [ReferenceDataBootstrapHostedService.EnabledKey] = "true",
            });

        ReferenceDataBootstrapHostedService hostedService = factory.Services
            .GetServices<IHostedService>()
            .OfType<ReferenceDataBootstrapHostedService>()
            .Single();

        await hostedService.Completed.WaitAsync(TimeSpan.FromMinutes(3));

        ReferenceDataBootstrapResult? result = hostedService.Result;
        result.Should().NotBeNull("the bootstrap was enabled for this host");
        result!.Ran.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Failures.Should().Be(0);
        result.CurrentWeek.Should().NotBeNull("the real calendar was ingested, so there is a current week");

        int season = result.Season;
        int week = result.CurrentWeek!.Value;

        await factory.ExecuteDbAsync(async db =>
        {
            (await db.SeasonWeeks.CountAsync(w => w.SeasonYear == season)).Should().BeGreaterThan(10);
            (await db.SeasonWeeks.CountAsync(w => w.SeasonYear == season && !w.IsRegularSeason))
                .Should().Be(1, "only conference-championship week is out of scope; bowls are never stored");
            (await db.Teams.CountAsync()).Should().BeGreaterThan(100);
            (await db.Conferences.CountAsync()).Should().BeGreaterThan(10);
            (await db.TeamAliases.CountAsync()).Should().BeGreaterThan(0);
            (await db.Games.CountAsync(g => g.SeasonYear == season && g.Week == week)).Should().BeGreaterThan(0);

            List<DataRefreshStatus> statuses = await db.DataRefreshStatuses.ToListAsync();
            statuses.Should().OnlyContain(status => status.LastError == null);
            statuses.Should().OnlyContain(status => status.LastSuccessUtc != null);
        });
    }

    /// <summary>Bearer token provider for this test only; production uses <c>CfbdAccessTokenProvider</c>.</summary>
    private sealed class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _token;

        public StaticAccessTokenProvider(string token)
        {
            _token = token;
        }

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_token);

        public AllowedHostsValidator AllowedHostsValidator { get; } = new();
    }
}
