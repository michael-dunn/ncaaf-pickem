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

        RouteGroupBuilder api = app.MapGroup("/api")
            .WithTags("api")
            // Every mutating /api call must carry X-Requested-With: NcaafPickEm.
            .RequireCsrfHeader();

        api.MapAdminEndpoints();
        api.MapCorrectionEndpoints();
        api.MapDashboardEndpoints();
        api.MapGameSetEndpoints();
        api.MapInviteEndpoints();
        api.MapLeaderboardEndpoints();
        api.MapLeagueEndpoints();
        api.MapPickEndpoints();
        api.MapPointRuleEndpoints();
        api.MapReferenceEndpoints();
        api.MapSeasonEndpoints();
        api.MapStatsEndpoints();

        // One line per feature, alphabetical.
        api.MapMeEndpoints();
        api.MapPushEndpoints();

        // Sign in as a fixture or simulator user without a tailnet (P2-05, P11-01). Never mapped
        // in Production; Testing needs it too so DevLoginTests can exercise the real route. Every
        // other development tool lives outside the app, in tools/NcaafPickEm.Simulator (D-182).
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.MapDevAuthEndpoints();
        }

        return app;
    }
}
