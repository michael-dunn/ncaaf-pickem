namespace NcaafPickEm.Shared.Contracts.Invites;

/// <summary>
/// Body of <c>GET /api/invites/{code}</c>, and the 409 body of accept when <paramref name="State"/>
/// is anything but Valid.
/// </summary>
/// <param name="LeagueName">League the invite is for.</param>
/// <param name="SeasonYear">Its season.</param>
/// <param name="MemberCount">Active members right now.</param>
/// <param name="State">Whether the caller can accept.</param>
public sealed record InvitePreview(
    string LeagueName,
    int SeasonYear,
    int MemberCount,
    InviteState State);
