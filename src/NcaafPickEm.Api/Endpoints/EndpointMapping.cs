using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// The one place endpoint groups are wired up. Each feature owns a
/// <c>&lt;Feature&gt;Endpoints.cs</c> file exposing <c>MapXxx(this RouteGroupBuilder)</c>
/// and adds exactly one line here, so parallel tasks do not fight over Program.cs.
/// </summary>
public static class EndpointMapping
{
    /// <summary>
    /// Maps the root-level endpoints and the <c>/api</c> group.
    /// </summary>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Health lives at the root, not under /api, so probes never need auth or the CSRF header.
        app.MapHealthEndpoints();

        // Sign-in and sign-out are browser navigations under /auth, not SPA fetches under /api.
        app.MapAuthEndpoints();

        RouteGroupBuilder api = app.MapGroup("/api")
            .WithTags("api")
            // Every mutating /api call must carry X-Requested-With: NcaafPickEm.
            .AddEndpointFilter<CsrfEndpointFilter>();

        api.MapSeasonEndpoints();

        // One line per feature, alphabetical. Later phases add:
        //   api.MapAdminEndpoints();         (P0-06)
        //   api.MapLeagueEndpoints();        (P1-01)
        //   api.MapGameSetEndpoints();       (P3-03)
        //   api.MapPickEndpoints();          (P4-01)
        //   api.MapLeaderboardEndpoints();   (P5-03)
        //   api.MapDashboardEndpoints();     (P6-02)
        //   api.MapPushEndpoints();          (P7-01)
        api.MapMeEndpoints();

        // Authorization-matrix probes. Never mapped in Production; deleted by P1-01.
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            api.MapDiagnosticsEndpoints();
        }

        return app;
    }
}
