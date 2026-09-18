using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Models;

/// <summary>
/// One game's live status from a score provider, in ESPN's shape (Feature 12). CFBD's fallback
/// scoreboard can only ever populate a subset (Scheduled/Final, no period or clock; D-012).
/// </summary>
/// <param name="SourceEventId">The provider's event id, e.g. ESPN's <c>events[].id</c>.</param>
/// <param name="KickoffUtc">Scheduled kickoff instant.</param>
/// <param name="HomeName">Home school name, no mascot (ESPN <c>team.location</c>).</param>
/// <param name="HomeAbbreviation">Home team's provider abbreviation.</param>
/// <param name="HomeSourceTeamId">The provider's home team id, when it has one.</param>
/// <param name="HomeScore">
/// Home score. Never meaningful while <see cref="Status"/> is <see cref="GameStatus.Scheduled"/> —
/// ESPN reports <c>"0"</c> for a game that has not started (D-012).
/// </param>
/// <param name="AwayName">Away school name, no mascot.</param>
/// <param name="AwayAbbreviation">Away team's provider abbreviation.</param>
/// <param name="AwaySourceTeamId">The provider's away team id, when it has one.</param>
/// <param name="AwayScore">Away score. Same caveat as <see cref="HomeScore"/>.</param>
/// <param name="Status">Status already mapped per 04-Domain-Algorithms.md section 9.</param>
/// <param name="RawStatusName">The provider's own status string, e.g. <c>"STATUS_FINAL"</c>.</param>
/// <param name="Completed">The provider's own completed flag, independent of <see cref="Status"/>.</param>
/// <param name="Period">Quarter or overtime period while in progress.</param>
/// <param name="Clock">Display clock, e.g. <c>"12:04"</c>.</param>
/// <param name="Spread">
/// Home-relative spread when the provider carries live odds; negative means home favored.
/// </param>
/// <param name="Source">
/// Which provider produced this update, which decides how the matcher identifies the game
/// (P2-03): ESPN's payload carries school names and its own event id, while the CFBD fallback
/// carries neither and is matched on <c>Games.CfbdGameId</c> in <see cref="SourceEventId"/>.
/// Defaults to <see cref="ProviderSource.Espn"/>, which is also right for the fixture provider
/// since its payloads are shaped like ESPN's.
/// </param>
public sealed record LiveScoreUpdate(
    string SourceEventId,
    DateTime KickoffUtc,
    string HomeName,
    string HomeAbbreviation,
    string? HomeSourceTeamId,
    int? HomeScore,
    string AwayName,
    string AwayAbbreviation,
    string? AwaySourceTeamId,
    int? AwayScore,
    GameStatus Status,
    string RawStatusName,
    bool Completed,
    byte? Period,
    string? Clock,
    decimal? Spread,
    ProviderSource Source = ProviderSource.Espn);
