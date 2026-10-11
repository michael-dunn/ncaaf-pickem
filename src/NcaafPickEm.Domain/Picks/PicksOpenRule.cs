using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Picks;

/// <summary>Where a week stands against its picks-open moment (Feature 16).</summary>
public enum PicksOpenState
{
    /// <summary>The picks-open moment has not arrived. Members see when picks will open, not the provisional set.</summary>
    NotYetOpen,

    /// <summary>
    /// The moment has passed but the week's set has no active games (generation failed or has not
    /// run): picks stay closed until the set is generated (Feature 16, D-190).
    /// </summary>
    AwaitingGames,

    /// <summary>The moment has passed and the set has games: picks behave as before, until lock.</summary>
    Open,
}

/// <summary>A week's picks-open moment and where now stands against it.</summary>
/// <param name="OpensAtUtc">The picks-open moment; null when no season calendar is known.</param>
/// <param name="State">Where now stands against it.</param>
public sealed record PicksOpening(DateTimeOffset? OpensAtUtc, PicksOpenState State)
{
    /// <summary>True when members may see the week's games and pick them (lock aside).</summary>
    public bool IsOpen => State == PicksOpenState.Open;
}

/// <summary>
/// The one answer to "have this week's picks opened?" (Feature 16, 04-Domain-Algorithms.md
/// section 4.1, D-190): Tuesday 04:00 ET of the week's window
/// (<see cref="SeasonCalendar.PicksOpenAtUtc"/>), and only once the set has games.
/// </summary>
/// <remarks>
/// Pure. Callers: <c>PickService</c> (refuses a pick before the moment, hides the provisional set
/// from the picks page), <c>GameSetService</c> (hides it from members on the week's games),
/// <c>RegenerateGameSetsJob</c> (never regenerates a week whose moment has passed),
/// <c>PicksOpenJob</c> (the "picks are open" push). Locking is separate: <c>WeekGameSetLockGuard</c>.
/// </remarks>
public static class PicksOpenRule
{
    /// <summary>The week's picks-open moment.</summary>
    /// <param name="week">The week.</param>
    /// <exception cref="ArgumentNullException"><paramref name="week"/> is null.</exception>
    public static DateTimeOffset OpensAtUtc(SeasonWeek week) => SeasonCalendar.PicksOpenAtUtc(week);

    /// <summary>
    /// True once the week's picks-open moment has arrived, whether or not the set has games. From
    /// then on no automatic regeneration may change the set.
    /// </summary>
    /// <param name="nowUtc">The caller's clock reading.</param>
    /// <param name="week">The week.</param>
    /// <exception cref="ArgumentNullException"><paramref name="week"/> is null.</exception>
    public static bool HasOpenMomentPassed(DateTimeOffset nowUtc, SeasonWeek week) => nowUtc >= OpensAtUtc(week);

    /// <summary>Where <paramref name="week"/>'s picks stand at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The caller's clock reading.</param>
    /// <param name="week">
    /// The week's calendar entry, or null when the season has no calendar (the rule cannot apply,
    /// so only the set decides).
    /// </param>
    /// <param name="hasActiveGames">True when the week's set exists and holds at least one active game.</param>
    public static PicksOpening At(DateTimeOffset nowUtc, SeasonWeek? week, bool hasActiveGames)
    {
        PicksOpenState generated = hasActiveGames ? PicksOpenState.Open : PicksOpenState.AwaitingGames;
        if (week is null)
        {
            return new PicksOpening(null, generated);
        }

        DateTimeOffset opensAtUtc = OpensAtUtc(week);
        return new PicksOpening(opensAtUtc, nowUtc < opensAtUtc ? PicksOpenState.NotYetOpen : generated);
    }
}
