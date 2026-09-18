namespace NcaafPickEm.Api;

/// <summary>
/// Single registration point for API-layer services (auth, validation, endpoint filters, JSON options).
/// Program.cs calls this once; later phases add registrations here, not in Program.cs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the HTTP layer needs.
    /// </summary>
    /// <remarks>
    /// Intentionally minimal in P0-01. Later phases add:
    /// <list type="bullet">
    ///   <item>P0-03 — cookie authentication + the Google handler, the three authorization policies
    ///         (<c>Authenticated</c>, <c>LeagueMember</c>, <c>LeagueCommissioner</c>),
    ///         <c>LeagueMembershipEndpointFilter</c> and the CSRF filter.</item>
    ///   <item>P1-01 onwards — FluentValidation validators from <c>Api/Validation</c>.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();

        return services;
    }
}
