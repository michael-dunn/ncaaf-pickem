using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the Leagues and members routes in 03-API-Contracts.md ("Leagues and members",
/// Feature 01). Pages depend on this interface, never on <see cref="HttpClient"/> directly, so a
/// Development build can swap in <see cref="Fakes.FakeLeaguesApi"/> before P1-01 merges (see
/// DECISIONS.md, the fake-API entry).
/// </summary>
public interface ILeaguesApi
{
    /// <summary><c>GET /api/leagues</c>. Every league the caller is an active member of.</summary>
    Task<LeagueSummary[]> GetMyLeaguesAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/leagues</c>.</summary>
    Task<LeagueDetail> CreateLeagueAsync(CreateLeagueRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}</c>.</summary>
    Task<LeagueDetail> GetLeagueAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>PUT /api/leagues/{leagueId}/settings</c>. Commissioner only.</summary>
    Task<LeagueDetail> UpdateSettingsAsync(
        Guid leagueId,
        UpdateLeagueSettingsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/members</c>.</summary>
    Task<MemberRow[]> GetMembersAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>PUT /api/leagues/{leagueId}/members/me/display-name</c>. 409 if taken in the league.</summary>
    Task SetMyDisplayNameAsync(
        Guid leagueId,
        SetLeagueDisplayNameRequest request,
        CancellationToken cancellationToken = default);

    /// <summary><c>DELETE /api/leagues/{leagueId}/members/{membershipId}</c>. Soft remove.</summary>
    Task RemoveMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/leagues/{leagueId}/members/{membershipId}/promote</c>.</summary>
    Task PromoteMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/leagues/{leagueId}/members/{membershipId}/demote</c>.</summary>
    Task DemoteMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/leagues/{leagueId}/commissioner/transfer</c>.</summary>
    Task TransferCommissionerAsync(Guid leagueId, Guid toMembershipId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/leagues/{leagueId}/invites</c>. Commissioner only.</summary>
    Task<InviteResponse> CreateInviteAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/invites</c>. Active invites only.</summary>
    Task<InviteResponse[]> GetInvitesAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>DELETE /api/leagues/{leagueId}/invites/{inviteId}</c>.</summary>
    Task RevokeInviteAsync(Guid leagueId, Guid inviteId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/invites/{code}</c>.</summary>
    Task<InvitePreview> GetInvitePreviewAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/invites/{code}/accept</c>. Never throws for a non-Valid state; see
    /// <see cref="InviteAcceptOutcome"/>.
    /// </summary>
    Task<InviteAcceptOutcome> AcceptInviteAsync(string code, CancellationToken cancellationToken = default);
}
