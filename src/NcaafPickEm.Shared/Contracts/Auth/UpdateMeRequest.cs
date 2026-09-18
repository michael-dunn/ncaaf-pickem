namespace NcaafPickEm.Shared.Contracts.Auth;

/// <summary>
/// Body of <c>PUT /api/me</c>.
/// </summary>
/// <param name="DisplayName">
/// New account-wide display name. 1 to 30 characters after trimming (Feature 08 Profile).
/// Per-league names are set through <c>PUT /api/leagues/{leagueId}/members/me/display-name</c>.
/// </param>
public sealed record UpdateMeRequest(string DisplayName);
