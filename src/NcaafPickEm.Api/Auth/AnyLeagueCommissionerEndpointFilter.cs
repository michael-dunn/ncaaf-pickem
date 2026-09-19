using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Admin scope: the caller must be a commissioner of <em>some</em> league.
/// </summary>
/// <remarks>
/// <c>/api/admin/*</c> reports on data that is not owned by any one league (provider freshness,
/// the CFBD call counter, job runs), so there is no <c>{leagueId}</c> to scope it with and
/// <see cref="LeagueMembershipEndpointFilter"/> does not apply. 03-API-Contracts.md scopes it to
/// "Commish (any league)". Nothing here reveals a league's existence, so a signed-in caller who
/// commissions nothing gets a plain 403 rather than the 404 a league route would answer with.
/// Apply through <c>RequireAnyLeagueCommissioner()</c>.
/// </remarks>
public sealed class AnyLeagueCommissionerEndpointFilter : IEndpointFilter
{
    private readonly bool _allowWhenNoLeaguesExist;

    /// <summary>Creates the filter.</summary>
    /// <param name="allowWhenNoLeaguesExist">
    /// When true, a signed-in caller is also allowed while no league exists anywhere in the
    /// database (D-166) — the bootstrap window on a fresh deployment, where nobody commissions
    /// anything yet and the data status page and manual refresh are the only way to get the
    /// season's reference data in by hand.
    /// </param>
    public AnyLeagueCommissionerEndpointFilter(bool allowWhenNoLeaguesExist = false)
    {
        _allowWhenNoLeaguesExist = allowWhenNoLeaguesExist;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        ICurrentUser currentUser = http.RequestServices.GetRequiredService<ICurrentUser>();
        if (currentUser.TryGetUserId() is not Guid userId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        AppDbContext database = http.RequestServices.GetRequiredService<AppDbContext>();

        bool isCommissioner = await database.Memberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId
                    && membership.RemovedUtc == null
                    && membership.Role == MembershipRole.Commissioner,
                http.RequestAborted);

        if (!isCommissioner && _allowWhenNoLeaguesExist)
        {
            // Not "the caller has no league" — "the database has no league", which is only ever
            // true before the first one is created.
            isCommissioner = !await database.Leagues.AsNoTracking().AnyAsync(http.RequestAborted);
        }

        if (!isCommissioner)
        {
            return TypedResults.Problem(
                title: "Commissioner only",
                detail: "This action is limited to commissioners.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
