using NcaafPickEm.Domain.Points;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// One row of a week's game set as <see cref="WeekLocker"/> sees it: enough to decide whether it
/// still counts, what it is worth, and what the line was when picks froze.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c>.</param>
/// <param name="AddedUtc">
/// When the row joined the set, for the "Submitted since the set last grew" half of
/// <c>04-Domain-Algorithms.md</c> section 4.
/// </param>
/// <param name="IsActive">
/// <c>!IsRemoved &amp;&amp; !IsVoided</c>. Inactive rows are passed in (the caller hands over the
/// whole set) but are neither snapshotted nor counted towards anyone's status.
/// </param>
/// <param name="Game">The matchup facts point resolution needs.</param>
/// <param name="PointValueOverride">The commissioner's manual value, or null when the rules decide.</param>
/// <param name="CurrentSpread">
/// The newest <c>GameLines.Spread</c> known right now, or null when the game has no line. This is
/// what is frozen as <c>SpreadAtLock</c> and what a close-spread rule resolves against.
/// </param>
public readonly record struct WeekLockGame(
    Guid GameSetGameId,
    DateTime AddedUtc,
    bool IsActive,
    PointGameInfo Game,
    int? PointValueOverride,
    decimal? CurrentSpread);
