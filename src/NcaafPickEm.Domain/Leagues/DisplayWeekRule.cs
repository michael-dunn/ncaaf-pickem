using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// The week the app treats as "this week" for one league (Feature 15, 04-Domain-Algorithms.md
/// section 12.1, D-193): the week just played, from the calendar's Sunday rollover until the next
/// week's picks open on Tuesday, and otherwise the calendar's current week.
/// </summary>
/// <remarks>
/// Pure. Only what members see uses it - "This week's leaderboard", the week navigation's jump,
/// the league home card. Jobs keep using <see cref="SeasonCalendar.CurrentWeekAt"/>.
/// </remarks>
public static class DisplayWeekRule
{
    /// <summary>Chooses the display week for one league.</summary>
    /// <param name="nowUtc">The caller's clock reading.</param>
    /// <param name="weeks">Every week of the league's season; empty when no calendar is ingested yet.</param>
    /// <param name="firstWeek">The league's first week.</param>
    /// <param name="lastWeek">The league's last week.</param>
    /// <param name="lockedWeeks">The league's weeks whose set has locked (played weeks).</param>
    /// <returns>A week in <paramref name="firstWeek"/>..<paramref name="lastWeek"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weeks"/> or <paramref name="lockedWeeks"/> is null.</exception>
    public static int At(
        DateTimeOffset nowUtc,
        IReadOnlyList<SeasonWeek> weeks,
        int firstWeek,
        int lastWeek,
        IReadOnlyCollection<int> lockedWeeks)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(lockedWeeks);

        if (weeks.Count == 0)
        {
            return firstWeek;
        }

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(nowUtc, weeks);
        int calendarWeek = Math.Clamp(current.Week, firstWeek, lastWeek);

        // Before the season, past the league's range, or after the season: nothing rolls over.
        if (current.State != SeasonState.InSeason || current.Week != calendarWeek)
        {
            return calendarWeek;
        }

        SeasonWeek window = weeks.First(week => week.Week == current.Week);
        if (PicksOpenRule.HasOpenMomentPassed(nowUtc, window))
        {
            return calendarWeek;
        }

        // The results window: the most recent played week, skipping a bye or gap week. A league's
        // first week has none, so it stays on its own week.
        int played = lockedWeeks
            .Where(week => week >= firstWeek && week < calendarWeek)
            .DefaultIfEmpty(calendarWeek)
            .Max();
        return played;
    }
}
