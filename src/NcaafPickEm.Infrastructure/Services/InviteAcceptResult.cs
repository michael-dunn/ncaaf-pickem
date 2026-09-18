using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Outcome of <c>InviteService.AcceptAsync</c>. Exactly one of <see cref="League"/> or
/// <see cref="Conflict"/> is set: the endpoint returns 200 with <see cref="League"/> when the
/// caller joined, or 409 with <see cref="Conflict"/> (whose <c>State</c> says why) otherwise.
/// </summary>
public sealed record InviteAcceptResult(LeagueDetail? League, InvitePreview? Conflict)
{
    /// <summary>True when the caller was added (or reactivated) as a member.</summary>
    public bool Succeeded => League is not null;
}
