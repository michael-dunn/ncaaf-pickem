namespace NcaafPickEm.Shared.Contracts.Invites;

/// <summary>An active invite, from <c>POST</c> and <c>GET /api/leagues/{leagueId}/invites</c>.</summary>
/// <param name="InviteId">The invite (used by revoke).</param>
/// <param name="Code">Six-digit shareable code, leading zeros allowed (P9-05).</param>
/// <param name="Url">Absolute join URL: <c>{App:PublicOrigin}/join/{Code}</c>.</param>
/// <param name="ExpiresUtc">Expiry instant.</param>
/// <param name="Uses">Accepted count so far.</param>
/// <param name="MaxUses">Maximum accepts.</param>
public sealed record InviteResponse(
    Guid InviteId,
    string Code,
    string Url,
    DateTimeOffset ExpiresUtc,
    int Uses,
    int MaxUses);
