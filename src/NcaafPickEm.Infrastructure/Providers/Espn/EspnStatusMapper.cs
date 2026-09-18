using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Espn;

/// <summary>
/// Maps ESPN's <c>status.type</c> block onto <see cref="GameStatus"/>
/// (04-Domain-Algorithms.md section 9, as corrected by the provider spike).
/// </summary>
/// <remarks>
/// ESPN's set of <c>status.type.name</c> values is open and has grown without notice before, so
/// an unrecognized name is never an exception and never a crash: it falls back to the coarse
/// three-value <c>status.type.state</c> ("pre", "in", "post"), which has been stable for years.
/// The caller logs the unknown name once so the table can be extended deliberately.
/// </remarks>
public static class EspnStatusMapper
{
    /// <summary>
    /// Maps a status block. Returns <see langword="null"/> only when neither the name nor the
    /// state could be understood, which the caller treats as "this event tells us nothing".
    /// </summary>
    /// <param name="typeName">ESPN <c>status.type.name</c>, e.g. <c>"STATUS_FINAL"</c>.</param>
    /// <param name="state">ESPN <c>status.type.state</c>: "pre", "in" or "post".</param>
    /// <param name="completed">ESPN <c>status.type.completed</c>.</param>
    /// <param name="recognized">
    /// True when <paramref name="typeName"/> was in the known table; false when the result came
    /// from the <paramref name="state"/> fallback (or from nothing at all).
    /// </param>
    public static GameStatus? Map(string? typeName, string? state, bool completed, out bool recognized)
    {
        recognized = true;

        switch (typeName)
        {
            case "STATUS_SCHEDULED":
            case "STATUS_PRE":
                return GameStatus.Scheduled;

            case "STATUS_IN_PROGRESS":
            case "STATUS_HALFTIME":
            case "STATUS_END_PERIOD":
            case "STATUS_END_OF_PERIOD":
            case "STATUS_DELAYED":
            case "STATUS_RAIN_DELAY":
                return GameStatus.InProgress;

            case "STATUS_FINAL":
            case "STATUS_FINAL_OVERTIME":
                return GameStatus.Final;

            case "STATUS_POSTPONED":
            case "STATUS_SUSPENDED":
                return GameStatus.Postponed;

            case "STATUS_CANCELED":
            case "STATUS_CANCELLED":
            case "STATUS_ABANDONED":
                return GameStatus.Cancelled;

            default:
                recognized = false;
                break;
        }

        // The state fallback. "post" without `completed` is the one genuinely ambiguous case:
        // the game is neither scheduled nor over, so it maps to InProgress, the only non-terminal
        // status. That can never fire GameWentFinal, and the apply service never regresses a game
        // that is already Final, so the worst case is one poll of a slightly wrong label.
        return state switch
        {
            "pre" => GameStatus.Scheduled,
            "in" => GameStatus.InProgress,
            "post" => completed ? GameStatus.Final : GameStatus.InProgress,
            _ => null,
        };
    }
}
