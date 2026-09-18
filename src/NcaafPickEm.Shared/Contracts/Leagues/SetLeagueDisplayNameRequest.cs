namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>
/// Body of <c>PUT /api/leagues/{leagueId}/members/me/display-name</c>; 409 when the effective name
/// is already taken in the league.
/// </summary>
/// <param name="DisplayName">Per-league name, 1..30 characters; null or empty clears the override.</param>
public sealed record SetLeagueDisplayNameRequest(string? DisplayName);
