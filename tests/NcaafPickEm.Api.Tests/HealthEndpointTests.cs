using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NcaafPickEm.Shared.Contracts.Health;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Proves the composition root boots under <c>WebApplicationFactory&lt;Program&gt;</c> and that the
/// probes answer. P0-02 extends this with a real database behind <c>/health/ready</c>.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("Jobs:Enabled", "false"));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task GivenRunningApi_WhenProbingHealth_ThenItReturnsOk(string route)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be(HealthResponse.Ok);
    }
}
