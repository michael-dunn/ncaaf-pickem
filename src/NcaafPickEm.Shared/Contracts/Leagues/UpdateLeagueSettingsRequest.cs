namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>Body of <c>PUT /api/leagues/{leagueId}/settings</c> (commissioner only).</summary>
/// <param name="Name">League name, 1..50 characters.</param>
/// <param name="FirstWeek">First playable week (regular season only).</param>
/// <param name="LastWeek">Last playable week (regular season only, not before <paramref name="FirstWeek"/>).</param>
/// <param name="DefaultPointValue">Default points per correct pick, 1..100.</param>
public sealed record UpdateLeagueSettingsRequest(
    string Name,
    int FirstWeek,
    int LastWeek,
    int DefaultPointValue);
