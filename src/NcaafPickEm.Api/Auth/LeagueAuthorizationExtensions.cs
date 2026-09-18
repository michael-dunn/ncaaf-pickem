using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// One-line league scoping for an endpoint group, and the accessor handlers read it back with.
/// </summary>
public static class LeagueAuthorizationExtensions
{
    /// <summary>
    /// Requires a signed-in caller with an active membership in the route's league.
    /// Anonymous gets 401, non-member gets 404.
    /// </summary>
    public static TBuilder RequireLeagueMember<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(PolicyNames.LeagueMember);
        builder.AddEndpointFilter(new LeagueMembershipEndpointFilter(requireCommissioner: false));
        return builder;
    }

    /// <summary>
    /// Requires a commissioner of the route's league.
    /// Anonymous gets 401, non-member gets 404, plain member gets 403.
    /// </summary>
    public static TBuilder RequireLeagueCommissioner<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(PolicyNames.LeagueCommissioner);
        builder.AddEndpointFilter(new LeagueMembershipEndpointFilter(requireCommissioner: true));
        return builder;
    }

    /// <summary>
    /// The membership <see cref="LeagueMembershipEndpointFilter"/> resolved for this request.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The endpoint did not apply <see cref="RequireLeagueMember"/> or
    /// <see cref="RequireLeagueCommissioner"/>. Handlers must never re-query the membership
    /// themselves (05-Conventions.md).
    /// </exception>
    public static Membership GetMembership(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Items[AuthDefaults.MembershipItemKey] as Membership
            ?? throw new InvalidOperationException(
                "No membership on this request. Apply RequireLeagueMember() or "
                + "RequireLeagueCommissioner() to the endpoint group.");
    }
}
