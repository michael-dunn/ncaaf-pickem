namespace NcaafPickEm.Api.Auth;

/// <summary>
/// One-line admin scoping for an endpoint group.
/// </summary>
public static class AdminAuthorizationExtensions
{
    /// <summary>
    /// Requires a signed-in caller who is a commissioner of at least one league.
    /// Anonymous gets 401, everyone else who commissions nothing gets 403.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint or group builder.</typeparam>
    /// <param name="builder">The endpoint or group to scope.</param>
    /// <param name="allowWhenNoLeaguesExist">
    /// When true, any signed-in caller is also allowed while the database holds no league at all
    /// (D-166). Set on the two bootstrap routes only: on a fresh deployment there is no
    /// commissioner yet, because creating the first league is what makes one.
    /// </param>
    public static TBuilder RequireAnyLeagueCommissioner<TBuilder>(
        this TBuilder builder,
        bool allowWhenNoLeaguesExist = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(PolicyNames.Authenticated);
        builder.AddEndpointFilter(new AnyLeagueCommissionerEndpointFilter(allowWhenNoLeaguesExist));
        builder.WithMetadata(new EndpointScopeMetadata(EndpointScope.AnyLeagueCommissioner));
        return builder;
    }
}
