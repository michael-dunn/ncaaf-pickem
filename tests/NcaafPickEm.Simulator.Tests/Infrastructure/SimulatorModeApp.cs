using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace NcaafPickEm.Simulator.Tests.Infrastructure;

/// <summary>
/// The real app (Program.cs, the real pipeline, cookie dev-login) in the mode its
/// <c>simulator</c> launch profile runs it: <c>Providers:*=Simulator</c>, over the simulator's
/// database, on the same clock as the simulator. Jobs stay off so the test decides when the lock
/// runs; the simulator's own "lock due" does it, as a real app's minute tick would.
/// </summary>
internal sealed class SimulatorModeApp : WebApplicationFactory<Program>
{
    public static readonly WebApplicationFactoryClientOptions HttpsWithCookies = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private readonly string _connectionString;
    private readonly TimeProvider _clock;

    public SimulatorModeApp(string connectionString, TimeProvider clock)
    {
        _connectionString = connectionString;
        _clock = clock;
    }

    /// <summary>A browser signed in through <c>/auth/dev-login</c> as a simulator player.</summary>
    public async Task<HttpClient> SignInAsync(string user)
    {
        HttpClient client = CreateClient(HttpsWithCookies);
        using HttpResponseMessage login = await client.GetAsync($"/auth/dev-login?user={user}");
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Requested-With", "NcaafPickEm");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Providers:ReferenceData", "Simulator");
        builder.UseSetting("Providers:LiveScores", "Simulator");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureTestServices(services => services.AddSingleton(_clock));
    }
}
