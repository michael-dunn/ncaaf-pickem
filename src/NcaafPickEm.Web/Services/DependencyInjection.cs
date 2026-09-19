using Microsoft.AspNetCore.Components;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Single registration point for the Blazor client's services, so <c>Program.cs</c> stays a
/// two-line composition root and parallel tasks do not fight over it.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the app shell state and the API <see cref="HttpClient"/> with the CSRF header
    /// handler and the 401 -&gt; login redirect handler.
    /// </summary>
    /// <param name="services">Client service collection.</param>
    /// <param name="baseAddress">The host base address the app was served from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// One plain scoped <see cref="HttpClient"/> with a hand-built handler chain, rather than
    /// <c>AddHttpClient</c>: <c>IHttpClientFactory</c> would pull
    /// <c>Microsoft.Extensions.Http</c> and its options/logging graph into a payload this task
    /// exists to keep small, and the client only ever talks to its own origin. Feature code
    /// injects <see cref="HttpClient"/> directly.
    /// </remarks>
    public static IServiceCollection AddWebServices(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        services.AddScoped<AppShellState>();

        services.AddScoped(sp => new HttpClient(CreateHandlerChain(sp))
        {
            BaseAddress = baseAddress,
        });

#if DEBUG && USE_FAKE_API
        // Development-only, never in a Release publish: DEBUG is off in Release, so this branch
        // cannot compile in even if USE_FAKE_API is also passed by mistake. See DECISIONS.md.
        services.AddScoped<ILeaguesApi, Fakes.FakeLeaguesApi>();
        services.AddScoped<ISeasonsApi, Fakes.FakeSeasonsApi>();
        services.AddScoped<IMeApi, Fakes.FakeMeApi>();
        services.AddScoped<Fakes.FakeGameSetStore>();
        services.AddScoped<IGameSetsApi, Fakes.FakeGameSetsApi>();
        services.AddScoped<IPointRulesApi, Fakes.FakePointRulesApi>();
        services.AddScoped<IAdminApi, Fakes.FakeAdminApi>();
        services.AddScoped<IPicksApi, Fakes.FakePicksApi>();
        services.AddScoped<IPushApi, Fakes.FakePushApi>();
        services.AddScoped<IDashboardApi, Fakes.FakeDashboardApi>();
        services.AddScoped<ICorrectionsApi, Fakes.FakeCorrectionsApi>();
        services.AddScoped<ILeaderboardApi, Fakes.FakeLeaderboardApi>();
#else
        services.AddScoped<ILeaguesApi, LeaguesApi>();
        services.AddScoped<ISeasonsApi, SeasonsApi>();
        services.AddScoped<IMeApi, MeApi>();
        services.AddScoped<IGameSetsApi, GameSetsApi>();
        services.AddScoped<IPointRulesApi, PointRulesApi>();
        services.AddScoped<IAdminApi, AdminApi>();
        services.AddScoped<IPicksApi, PicksApi>();
        services.AddScoped<IPushApi, PushApi>();
        services.AddScoped<IDashboardApi, DashboardApi>();
        services.AddScoped<ICorrectionsApi, CorrectionsApi>();
        services.AddScoped<ILeaderboardApi, LeaderboardApi>();
#endif

        return services;
    }

    // Outermost handler runs first on the way out, last on the way back:
    // CSRF stamps the request, then the redirect handler inspects the response.
    private static HttpMessageHandler CreateHandlerChain(IServiceProvider services) =>
        new CsrfHeaderHandler
        {
            InnerHandler = new UnauthorizedRedirectHandler(services.GetRequiredService<NavigationManager>())
            {
                // In WebAssembly this resolves to the browser fetch handler.
                InnerHandler = new HttpClientHandler(),
            },
        };
}
