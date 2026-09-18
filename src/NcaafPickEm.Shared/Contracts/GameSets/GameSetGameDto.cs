using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>One game inside a league's week game set, as every game-bearing endpoint renders it.</summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c>; null in previews (no row yet).</param>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="HomeTeam">Home team.</param>
/// <param name="AwayTeam">Away team.</param>
/// <param name="HomeRank">AP rank for this week, when ranked.</param>
/// <param name="AwayRank">AP rank for this week, when ranked.</param>
/// <param name="KickoffUtc">Scheduled kickoff.</param>
/// <param name="PointValue">Resolved point value (frozen at lock).</param>
/// <param name="IsPointValueElevated">True when the value exceeds the league default.</param>
/// <param name="Source">Rule or Manual.</param>
/// <param name="Status">Live status.</param>
/// <param name="HomeScore">Live or final score.</param>
/// <param name="AwayScore">Live or final score.</param>
/// <param name="Period">Live period, when in progress.</param>
/// <param name="Clock">Live clock, when in progress.</param>
/// <param name="IsVoided">Excluded from scoring after lock (Feature 06).</param>
/// <param name="WinnerTeamId">Override winner, else the higher score when Final; null while pending or tied.</param>
public sealed record GameSetGameDto(
    Guid? GameSetGameId,
    Guid GameId,
    TeamDto HomeTeam,
    TeamDto AwayTeam,
    int? HomeRank,
    int? AwayRank,
    DateTimeOffset KickoffUtc,
    int PointValue,
    bool IsPointValueElevated,
    GameSetGameSource Source,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    byte? Period,
    string? Clock,
    bool IsVoided,
    Guid? WinnerTeamId);
