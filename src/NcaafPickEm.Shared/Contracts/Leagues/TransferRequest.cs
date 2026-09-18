namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>
/// Body of <c>POST /api/leagues/{leagueId}/commissioner/transfer</c>: promotes the target and
/// demotes the caller; other commissioners are untouched.
/// </summary>
/// <param name="ToMembershipId">Active membership to promote.</param>
public sealed record TransferRequest(Guid ToMembershipId);
