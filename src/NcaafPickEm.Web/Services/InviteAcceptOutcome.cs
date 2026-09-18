using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Result of <see cref="ILeaguesApi.AcceptInviteAsync"/>. <c>POST /api/invites/{code}/accept</c>
/// answers 200 with <see cref="LeagueDetail"/> on success, or 409 with an <see cref="InvitePreview"/>
/// body carrying the reason (Expired, Revoked, Full, AlreadyMember) on any other state
/// (03-API-Contracts.md). Exactly one of the two properties is set.
/// </summary>
/// <param name="League">The joined league, when the accept succeeded.</param>
/// <param name="Preview">The non-Valid preview, when the accept was refused.</param>
public sealed record InviteAcceptOutcome(LeagueDetail? League, InvitePreview? Preview);
