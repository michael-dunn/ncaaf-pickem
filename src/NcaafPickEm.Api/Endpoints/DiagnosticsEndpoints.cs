using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Two do-nothing league-scoped routes that exist so the authorization matrix has something to
/// run against before any real league endpoint exists.
/// </summary>
/// <remarks>
/// Mapped only in Development and Testing, never in Production. **P1-01 deletes this file** and
/// points <c>AuthMatrixTests</c> at the real <c>/api/leagues/{leagueId}</c> and
/// <c>/api/leagues/{leagueId}/settings</c> routes instead.
/// </remarks>
public static class DiagnosticsEndpoints
{
    /// <summary>Route of the member-scoped probe.</summary>
    public const string MemberRoute = "/api/leagues/{leagueId}/ping";

    /// <summary>Route of the commissioner-scoped probe.</summary>
    public const string CommissionerRoute = "/api/leagues/{leagueId}/ping/commish";

    /// <summary>Maps the two probes.</summary>
    public static RouteGroupBuilder MapDiagnosticsEndpoints(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGroup("/leagues/{leagueId:guid}/ping")
            .WithTags("diagnostics")
            .RequireLeagueMember()
            .MapGet("", Ping)
            .WithName("DiagnosticsPingMember");

        builder.MapGroup("/leagues/{leagueId:guid}/ping/commish")
            .WithTags("diagnostics")
            .RequireLeagueCommissioner()
            .MapGet("", Ping)
            .WithName("DiagnosticsPingCommissioner");

        return builder;
    }

    /// <summary>Echoes the membership the endpoint filter resolved, proving the scoping ran.</summary>
    private static Ok<DiagnosticsPing> Ping(HttpContext httpContext)
    {
        Membership membership = httpContext.GetMembership();
        return TypedResults.Ok(new DiagnosticsPing(membership.Id, membership.LeagueId, membership.Role.ToString()));
    }
}
