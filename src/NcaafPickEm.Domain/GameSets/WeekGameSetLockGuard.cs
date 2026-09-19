using System.Linq.Expressions;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// The one answer to "may this week still be changed?" (04-Domain-Algorithms.md sections 2, 3
/// and 4). A week freezes at its lock instant, not when the lock job happens to get to it, so
/// configuration and picks stop at exactly the same moment.
/// </summary>
/// <remarks>
/// Callers: <c>GameSetService</c> (generate, manual add and remove, week rules),
/// <c>PointRuleService</c> (per-game override, and the re-resolve sweep through
/// <see cref="IsNotFrozen"/>), and <c>PickService</c> (set pick, submit). Keeping them on one
/// predicate is what stops "locked" meaning two different things in two services (D-110).
/// </remarks>
public static class WeekGameSetLockGuard
{
    /// <summary>
    /// True once the week may no longer be changed: the lock job has written
    /// <see cref="WeekGameSet.LockedUtc"/>, or <see cref="WeekGameSet.LockAtUtc"/> has simply
    /// arrived and the job has not caught up yet.
    /// </summary>
    /// <param name="set">The week's set.</param>
    /// <param name="nowUtc">The caller's clock reading, in UTC.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is null.</exception>
    public static bool IsFrozen(WeekGameSet set, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(set);

        return set.LockedUtc is not null || (set.LockAtUtc is DateTime lockAtUtc && nowUtc >= lockAtUtc);
    }

    /// <summary>
    /// The negation of <see cref="IsFrozen"/> as a LINQ predicate, so a query that sweeps a
    /// league's still-editable weeks uses the same rule rather than a hand-written copy of it.
    /// </summary>
    /// <param name="nowUtc">The caller's clock reading, in UTC.</param>
    public static Expression<Func<WeekGameSet, bool>> IsNotFrozen(DateTime nowUtc) =>
        set => set.LockedUtc == null && (set.LockAtUtc == null || set.LockAtUtc > nowUtc);
}
