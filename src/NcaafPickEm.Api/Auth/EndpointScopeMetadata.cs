namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Which of the scoping helpers in this folder an endpoint was wired up with.
/// </summary>
/// <remarks>
/// The scope itself is enforced by an <see cref="IEndpointFilter"/>, and a filter leaves no trace
/// in <c>Endpoint.Metadata</c>. P8-01's route inventory test has to be able to answer "does every
/// <c>/api/leagues/{leagueId}/**</c> route carry the membership filter?" from
/// <c>EndpointDataSource</c> alone, so each helper also stamps this marker.
/// </remarks>
public enum EndpointScope
{
    /// <summary>Any signed-in caller (<c>RequireAuthorization(PolicyNames.Authenticated)</c>).</summary>
    Authenticated = 0,

    /// <summary>An active member of the route's league (<c>RequireLeagueMember()</c>).</summary>
    LeagueMember = 1,

    /// <summary>A commissioner of the route's league (<c>RequireLeagueCommissioner()</c>).</summary>
    LeagueCommissioner = 2,

    /// <summary>A commissioner of some league (<c>RequireAnyLeagueCommissioner()</c>).</summary>
    AnyLeagueCommissioner = 3,
}

/// <summary>
/// Endpoint metadata recording the <see cref="EndpointScope"/> an endpoint was scoped with.
/// </summary>
/// <param name="Scope">The scope the endpoint's filter enforces.</param>
public sealed record EndpointScopeMetadata(EndpointScope Scope);

/// <summary>
/// Endpoint metadata recording that <see cref="CsrfEndpointFilter"/> covers this endpoint.
/// </summary>
public sealed record CsrfProtectedMetadata
{
    /// <summary>The single instance; the marker carries no state.</summary>
    public static readonly CsrfProtectedMetadata Instance = new();
}
