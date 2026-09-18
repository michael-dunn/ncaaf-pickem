using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Resolves <c>{leagueId}</c> to the caller's active membership once per request and stashes it in
/// <see cref="HttpContext.Items"/> for the handler to read through <c>GetMembership()</c>.
/// </summary>
/// <remarks>
/// Feature 08 authorization, and the "do not reveal existence" rule in <c>03-API-Contracts.md</c>:
/// a caller with no active membership gets **404**, not 403, so probing league ids tells them
/// nothing. A member who hits a commissioner route gets **403**, because they already know the
/// league exists. Apply through <c>RequireLeagueMember()</c> / <c>RequireLeagueCommissioner()</c>.
/// </remarks>
public sealed class LeagueMembershipEndpointFilter : IEndpointFilter
{
    /// <summary>Route value the filter reads the league id from.</summary>
    public const string RouteValueName = "leagueId";

    private readonly bool _requireCommissioner;

    /// <summary>Creates the filter.</summary>
    /// <param name="requireCommissioner">
    /// When true, an active member whose role is not Commissioner is refused with 403.
    /// </param>
    public LeagueMembershipEndpointFilter(bool requireCommissioner)
    {
        _requireCommissioner = requireCommissioner;
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

        if (http.Request.RouteValues.TryGetValue(RouteValueName, out object? raw)
            && Guid.TryParse(raw?.ToString(), out Guid leagueId))
        {
            AppDbContext database = http.RequestServices.GetRequiredService<AppDbContext>();

            Membership? membership = await database.Memberships
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.LeagueId == leagueId
                        && candidate.UserId == userId
                        && candidate.RemovedUtc == null,
                    http.RequestAborted);

            if (membership is not null)
            {
                if (_requireCommissioner && membership.Role != MembershipRole.Commissioner)
                {
                    return TypedResults.Problem(
                        title: "Commissioner only",
                        detail: "This action is limited to commissioners of the league.",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                http.Items[AuthDefaults.MembershipItemKey] = membership;
                return await next(context);
            }
        }

        return NotFound();
    }

    private static IResult NotFound() => TypedResults.Problem(
        title: "League not found",
        detail: "No league with that id is visible to you.",
        statusCode: StatusCodes.Status404NotFound);
}
