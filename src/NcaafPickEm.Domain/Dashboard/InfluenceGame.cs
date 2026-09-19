using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// One game in the locked week's set, flattened to only what the influence dashboard needs
/// (Feature 05). A slim input record rather than the <c>WeekGameSetGame</c> / <c>Game</c> entities
/// keeps <see cref="InfluenceCalculator"/> pure, and lets a test build a week without a database.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames</c> row id. Identifies the game everywhere
/// else in the dashboard - picks point at it, and the output is keyed by it.</param>
/// <param name="GameId">The underlying <c>Games</c> row, for the caller's own joins.</param>
/// <param name="HomeTeamId">Home team.</param>
/// <param name="AwayTeamId">Away team.</param>
/// <param name="KickoffUtc">Kickoff, the third ordering key.</param>
/// <param name="PointValue">The point value frozen at lock, the second ordering key and what
/// <c>SwingPoints</c>, <c>PointsSoFar</c> and <c>MaxRemaining</c> are measured in.</param>
/// <param name="Status">The game's current status.</param>
/// <param name="HomeScore">Home points, or null when not reported.</param>
/// <param name="AwayScore">Away points, or null when not reported.</param>
/// <param name="ResultOverrideWinnerTeamId">The commissioner's winner correction, when set.</param>
/// <param name="IsVoided">Excluded from scoring after lock. A voided game is left out of the
/// dashboard entirely - see <see cref="InfluenceCalculator"/>.</param>
public sealed record InfluenceGame(
    Guid GameSetGameId,
    Guid GameId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTime KickoffUtc,
    int PointValue,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    Guid? ResultOverrideWinnerTeamId,
    bool IsVoided);
