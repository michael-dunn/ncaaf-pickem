using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Providers.Espn;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// ESPN's CDN rejects requests without a User-Agent with 403 (seen on the first deployment as a
/// poller that never received a score). The typed client must always send one.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class EspnClientHeadersTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task GivenTheEspnClient_WhenCreated_ThenItSendsAUserAgentAndAcceptsJson()
    {
        await using var factory = new ApiFactory(
            fixture.Database.ConnectionString,
            settings: new Dictionary<string, string> { ["Providers:LiveScores"] = "Espn" });

        using IServiceScope scope = factory.Services.CreateScope();
        var clients = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        using HttpClient client = clients.CreateClient(nameof(EspnLiveScoreProvider));

        client.DefaultRequestHeaders.UserAgent.ToString().Should().Be(EspnLiveScoreProvider.UserAgent);
        client.DefaultRequestHeaders.Accept.Should().Contain(a => a.MediaType == "application/json");
        client.BaseAddress.Should().Be(new Uri(EspnLiveScoreProvider.DefaultBaseAddress));
    }
}
