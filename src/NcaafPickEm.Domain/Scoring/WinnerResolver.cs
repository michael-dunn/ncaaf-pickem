using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// The one rule for "who won this game": a commissioner's result override beats everything, then
/// the higher score once the game is <see cref="GameStatus.Final"/>, and otherwise nobody
/// (<c>04-Domain-Algorithms.md</c> sections 6 and 7). Pure: no clock, no I/O, no entities.
/// </summary>
/// <remarks>
/// A Final game with equal scores, or with a score missing on either side, has <em>no</em> winner.
/// That is the "needs review" path - the game stays unscored and shows as pending on the dashboard
/// until a commissioner overrides or voids it - not a loss for everybody. Every caller that needs a
/// winner (the dashboard calculator, the week scorer, the game-set DTO mapper, the live-score
/// apply service) goes through here so the four can never drift apart.
/// </remarks>
public static class WinnerResolver
{
    /// <summary>
    /// Resolves the winning team of one game.
    /// </summary>
    /// <param name="resultOverrideWinnerTeamId">The commissioner's correction for this game in this
    /// league's week, or null when the scoreboard decides. Returned as-is when set, whatever the
    /// status and scores say.</param>
    /// <param name="status">The game's current status. Scores are read only when Final.</param>
    /// <param name="homeScore">Home points, or null when the provider has not reported them.</param>
    /// <param name="awayScore">Away points, or null when the provider has not reported them.</param>
    /// <param name="homeTeamId">Home team.</param>
    /// <param name="awayTeamId">Away team.</param>
    /// <returns>The winning team, or null when no winner can be determined.</returns>
    public static Guid? Resolve(
        Guid? resultOverrideWinnerTeamId,
        GameStatus status,
        int? homeScore,
        int? awayScore,
        Guid homeTeamId,
        Guid awayTeamId)
    {
        if (resultOverrideWinnerTeamId is { } overridden)
        {
            return overridden;
        }

        if (status != GameStatus.Final || homeScore is not { } home || awayScore is not { } away)
        {
            return null;
        }

        if (home == away)
        {
            return null;
        }

        return home > away ? homeTeamId : awayTeamId;
    }

    /// <summary>
    /// Whether <see cref="Resolve"/> can name a winner. The readable form of "this game is scoreable"
    /// for callers that do not care who won.
    /// </summary>
    /// <param name="resultOverrideWinnerTeamId">The commissioner's correction, when set.</param>
    /// <param name="status">The game's current status.</param>
    /// <param name="homeScore">Home points, or null.</param>
    /// <param name="awayScore">Away points, or null.</param>
    /// <returns>True when a winner can be determined.</returns>
    public static bool HasDeterminableWinner(
        Guid? resultOverrideWinnerTeamId,
        GameStatus status,
        int? homeScore,
        int? awayScore) =>
        resultOverrideWinnerTeamId is not null
        || (status == GameStatus.Final
            && homeScore is { } home
            && awayScore is { } away
            && home != away);
}
