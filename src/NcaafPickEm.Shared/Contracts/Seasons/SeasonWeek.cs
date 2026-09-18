namespace NcaafPickEm.Shared.Contracts.Seasons;

/// <summary>
/// One week of a season, as returned by <c>GET /api/seasons/{year}/weeks</c>
/// (03-API-Contracts.md, Feature 13).
/// </summary>
/// <param name="Week">Provider week number. Week 0 exists in some seasons.</param>
/// <param name="StartUtc">Sunday 00:00:00 Eastern for this week, in UTC.</param>
/// <param name="EndUtc">Saturday 23:59:59.999 Eastern for this week, in UTC. Inclusive.</param>
/// <param name="IsRegularSeason">False for conference championship week; only regular weeks are playable.</param>
public sealed record SeasonWeek(
    int Week,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsRegularSeason);
