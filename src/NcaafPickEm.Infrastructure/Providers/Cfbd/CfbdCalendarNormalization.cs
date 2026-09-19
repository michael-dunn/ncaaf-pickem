using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// Normalizes one CFBD calendar week to the Sunday-Saturday Eastern <see cref="SeasonWeek"/>
/// window <c>04-Domain-Algorithms.md</c> section 1 was written against, instead of storing
/// CFBD's own Monday 03:00 ET-through-next-Monday-02:59-ET boundary verbatim (D-013, and the
/// decision this class exists to implement — see DECISIONS.md).
/// </summary>
/// <remarks>
/// Public and static so it can be unit-tested directly against a synthetic Monday-03:00-ET input
/// without a database or a live call, per the P2-02 card's done-when line.
/// </remarks>
public static class CfbdCalendarNormalization
{
    /// <summary>
    /// Finds the Saturday inside <paramref name="week"/>'s CFBD window and returns the
    /// <see cref="SeasonWeek"/> for <see cref="SeasonCalendar.WeekWindow"/> of that Saturday.
    /// </summary>
    public static SeasonWeek Normalize(ProviderCalendarWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        DateOnly saturday = FindSaturdayEastern(week.StartUtc, week.EndUtc);
        (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(saturday);
        bool isRegularSeason = string.Equals(week.SeasonType, "regular", StringComparison.OrdinalIgnoreCase);

        return new SeasonWeek(week.Season, week.Week, startUtc, endUtc, isRegularSeason);
    }

    /// <summary>
    /// The Eastern calendar date of the one Saturday inside [<paramref name="startUtc"/>,
    /// <paramref name="endUtc"/>]. CFBD's own week window always contains exactly one.
    /// </summary>
    public static DateOnly FindSaturdayEastern(DateTime startUtc, DateTime endUtc)
    {
        DateTimeOffset startOffset = new(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc));
        DateTimeOffset endOffset = new(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc));

        DateOnly cursor = DateOnly.FromDateTime(SeasonCalendar.ToEastern(startOffset).DateTime);
        DateOnly endEastern = DateOnly.FromDateTime(SeasonCalendar.ToEastern(endOffset).DateTime);

        for (; cursor <= endEastern; cursor = cursor.AddDays(1))
        {
            if (cursor.DayOfWeek == DayOfWeek.Saturday)
            {
                return cursor;
            }
        }

        throw new ArgumentException(
            $"No Saturday found between {startOffset:O} and {endOffset:O}; this is not a CFBD-shaped week window.",
            nameof(endUtc));
    }
}
