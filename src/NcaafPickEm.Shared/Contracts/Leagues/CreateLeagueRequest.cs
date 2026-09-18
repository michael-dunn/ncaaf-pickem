namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>Body of <c>POST /api/leagues</c>.</summary>
/// <param name="Name">League name, 1..50 characters after trimming.</param>
/// <param name="SeasonYear">The one season this league covers.</param>
/// <param name="FirstWeek">First playable week; null = season default (1). Clamped to regular-season weeks.</param>
/// <param name="LastWeek">Last playable week; null = final regular-season week.</param>
public sealed record CreateLeagueRequest(
    string Name,
    int SeasonYear,
    int? FirstWeek,
    int? LastWeek);
