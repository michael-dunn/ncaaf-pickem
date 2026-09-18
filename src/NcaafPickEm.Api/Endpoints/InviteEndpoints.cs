using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Shareable join codes (Feature 01, P1-01): the league-scoped CRUD under
/// <c>/api/leagues/{leagueId}/invites</c> and the caller-facing <c>/api/invites/{code}</c> routes.
/// </summary>
public static class InviteEndpoints
{
    /// <summary>Maps every invite route.</summary>
    public static RouteGroupBuilder MapInviteEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagueInvites = api.MapGroup("/leagues/{leagueId:guid}/invites")
            .WithTags("invites");

        leagueInvites.MapPost("", CreateAsync)
            .WithName("InviteCreate")
            .RequireLeagueCommissioner();

        leagueInvites.MapGet("", ListAsync)
            .WithName("InviteListActive")
            .RequireLeagueCommissioner();

        leagueInvites.MapDelete("/{inviteId:guid}", RevokeAsync)
            .WithName("InviteRevoke")
            .RequireLeagueCommissioner();

        RouteGroupBuilder invites = api.MapGroup("/invites")
            .WithTags("invites")
            .RequireAuthorization(PolicyNames.Authenticated);

        invites.MapGet("/{code}", PreviewAsync)
            .WithName("InvitePreview");

        invites.MapPost("/{code}/accept", AcceptAsync)
            .WithName("InviteAccept");

        return api;
    }

    private static async Task<Ok<InviteResponse>> CreateAsync(
        HttpContext httpContext,
        InviteService inviteService,
        CancellationToken cancellationToken)
    {
        Membership commissioner = httpContext.GetMembership();
        InviteResponse response = await inviteService.CreateAsync(commissioner, RequestOrigin(httpContext), cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<InviteResponse[]>> ListAsync(
        HttpContext httpContext,
        InviteService inviteService,
        CancellationToken cancellationToken)
    {
        Membership commissioner = httpContext.GetMembership();
        InviteResponse[] invites = await inviteService.ListActiveAsync(
            commissioner.LeagueId, RequestOrigin(httpContext), cancellationToken);
        return TypedResults.Ok(invites);
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid inviteId,
        HttpContext httpContext,
        InviteService inviteService,
        CancellationToken cancellationToken)
    {
        Membership commissioner = httpContext.GetMembership();
        bool revoked = await inviteService.RevokeAsync(commissioner.LeagueId, inviteId, cancellationToken);
        return revoked ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<InvitePreview>, NotFound>> PreviewAsync(
        string code,
        ICurrentUser currentUser,
        InviteService inviteService,
        CancellationToken cancellationToken)
    {
        InvitePreview? preview = await inviteService.PreviewAsync(code, currentUser.UserId, cancellationToken);
        return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
    }

    private static async Task<Results<Ok<LeagueDetail>, Conflict<InvitePreview>, NotFound>> AcceptAsync(
        string code,
        ICurrentUser currentUser,
        InviteService inviteService,
        CancellationToken cancellationToken)
    {
        InviteAcceptResult? result = await inviteService.AcceptAsync(code, currentUser.UserId, cancellationToken);

        if (result is null)
        {
            return TypedResults.NotFound();
        }

        return result.Succeeded
            ? TypedResults.Ok(result.League!)
            : TypedResults.Conflict(result.Conflict!);
    }

    private static string RequestOrigin(HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
}
