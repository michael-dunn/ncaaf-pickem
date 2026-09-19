namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>Body of <c>POST /api/admin/unmatched/{id}/resolve</c>.</summary>
/// <param name="GameId">The <c>Games</c> row the unmatched provider game is actually.</param>
public sealed record ResolveUnmatchedRequest(Guid GameId);
