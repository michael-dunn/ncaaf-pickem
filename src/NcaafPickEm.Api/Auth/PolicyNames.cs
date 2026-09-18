namespace NcaafPickEm.Api.Auth;

/// <summary>
/// The three authorization policies from <c>01-Architecture.md</c>.
/// </summary>
/// <remarks>
/// All three only assert "signed in": the league scope itself cannot be decided by a policy
/// because it depends on a route value. <see cref="LeagueMembershipEndpointFilter"/> resolves
/// <c>{leagueId}</c> to the caller's active membership and decides 404 or 403 from there.
/// Apply them through <c>RequireLeagueMember()</c> / <c>RequireLeagueCommissioner()</c>, never by
/// hand, so the policy and the filter cannot drift apart.
/// </remarks>
public static class PolicyNames
{
    /// <summary>Any signed-in user.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Signed in, and an active member of the league in the route.</summary>
    public const string LeagueMember = "LeagueMember";

    /// <summary>Signed in, and a commissioner of the league in the route.</summary>
    public const string LeagueCommissioner = "LeagueCommissioner";
}
