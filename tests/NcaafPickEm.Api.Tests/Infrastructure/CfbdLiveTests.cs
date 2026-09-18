using CollegeFootballData;
using CollegeFootballData.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

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
