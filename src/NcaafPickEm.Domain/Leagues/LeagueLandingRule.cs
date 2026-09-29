using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// Which screen the app opens a league on (04-Domain-Algorithms.md section 12, D-187): Picks
/// while the current week is open, the Dashboard once it locks, and the Leaderboard once its
/// games are done and through the Sunday after a played week.
/// </summary>
/// <remarks>
/// Pure: the caller loads the league's current week's set and the one before it. "Locked" is
/// <see cref="WeekGameSetLockGuard.IsFrozen"/>, the same answer the pick endpoints give, so the
/// app never opens on Picks for a week that would refuse the pick.
/// </remarks>
public static class LeagueLandingRule
{
    /// <summary>Chooses the landing screen for one league.</summary>
    /// <param name="nowUtc">The caller's clock reading.</param>
    /// <param name="weeks">Every week of the league's season; empty when no calendar is ingested yet.</param>
    /// <param name="firstWeek">The league's first week.</param>
    /// <param name="lastWeek">The league's last week.</param>
    /// <param name="currentSet">The league's set for the current week, or null when it has none.</param>
    /// <param name="previousSet">The league's set for the week before the current one, or null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weeks"/> is null.</exception>
    public static LeagueLanding Choose(
        DateTimeOffset nowUtc,
        IReadOnlyList<SeasonWeek> weeks,
        int firstWeek,
        int lastWeek,
        WeekGameSet? currentSet,
        WeekGameSet? previousSet)
    {
        ArgumentNullException.ThrowIfNull(weeks);

        SeasonWeek? currentWindow = null;
        if (weeks.Count > 0)
        {
            if (weeks.Any(week => week.Week == lastWeek)
                && SeasonCalendar.LeagueIsCompleteAt(nowUtc, lastWeek, weeks))
            {
                return LeagueLanding.Leaderboard;
            }

            CurrentWeek current = SeasonCalendar.CurrentWeekAt(nowUtc, weeks);
            if (current.Week < firstWeek)
            {
                return LeagueLanding.LeagueHome;
            }

            currentWindow = weeks.FirstOrDefault(week => week.Week == current.Week);
        }

        if (currentSet is not null && WeekGameSetLockGuard.IsFrozen(currentSet, nowUtc.UtcDateTime))
        {
            return currentSet.IsComplete ? LeagueLanding.Leaderboard : LeagueLanding.Dashboard;
        }

        // Sunday: the next week has opened, but the weekend's results are what members come back
        // to see. Only after a week that was actually played (its set locked), so a league's
        // first week still opens on Picks.
        if (previousSet is { IsLocked: true }
            && currentWindow is not null
            && SeasonCalendar.IsOpeningDayAt(nowUtc, currentWindow))
        {
            return LeagueLanding.Leaderboard;
        }

        return LeagueLanding.Picks;
    }
}
