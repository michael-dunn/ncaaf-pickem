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
    /// <summary>CFBD's <c>seasonType</c> for the weeks this product plays.</summary>
    private const string RegularSeasonType = "regular";

    /// <summary>
    /// Turns one season's whole CFBD calendar into the <c>SeasonWeeks</c> rows to store (D-167):
    /// postseason rows are dropped, a repeated week number keeps the first row, and the highest
    /// remaining week is flagged <see cref="SeasonWeek.IsRegularSeason"/> <c>false</c> because it
    /// is conference-championship week (<c>04-Domain-Algorithms.md</c> section 1).
    /// </summary>
    /// <param name="providerWeeks">Every row CFBD's <c>/calendar</c> returned for the season.</param>
    public static CalendarNormalizationResult NormalizeSeason(IReadOnlyList<ProviderCalendarWeek> providerWeeks)
    {
        ArgumentNullException.ThrowIfNull(providerWeeks);

        List<SeasonWeek> weeks = [];
        List<int> duplicateWeeks = [];
        HashSet<int> seenWeeks = [];
        int skippedNonRegular = 0;

        foreach (ProviderCalendarWeek providerWeek in providerWeeks)
        {
            if (!IsRegularSeasonType(providerWeek.SeasonType))
            {
                skippedNonRegular++;
                continue;
            }

            if (!seenWeeks.Add(providerWeek.Week))
            {
                duplicateWeeks.Add(providerWeek.Week);
                continue;
            }

            weeks.Add(Normalize(providerWeek));
        }

        if (weeks.Count > 0)
        {
            int championshipWeek = weeks.Max(week => week.Week);
            weeks =
            [
                .. weeks.Select(week => week.Week == championshipWeek
                    ? week with { IsRegularSeason = false }
                    : week),
            ];
        }

        return new CalendarNormalizationResult(weeks, skippedNonRegular, duplicateWeeks);
    }

    /// <summary>
    /// True when CFBD called this week a regular-season week. Case-insensitive: the Kiota client
    /// hands the enum's own spelling (<c>"Regular"</c>) back, while the raw JSON says
    /// <c>"regular"</c>.
    /// </summary>
    /// <param name="seasonType">CFBD's <c>seasonType</c>, however it is spelled.</param>
    public static bool IsRegularSeasonType(string? seasonType) =>
        string.Equals(seasonType?.Trim(), RegularSeasonType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the Saturday inside <paramref name="week"/>'s CFBD window and returns the
    /// <see cref="SeasonWeek"/> for <see cref="SeasonCalendar.WeekWindow"/> of that Saturday.
    /// </summary>
    public static SeasonWeek Normalize(ProviderCalendarWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        DateOnly saturday = FindSaturdayEastern(week.StartUtc, week.EndUtc);
        (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(saturday);
        return new SeasonWeek(week.Season, week.Week, startUtc, endUtc, IsRegularSeasonType(week.SeasonType));
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

/// <summary>
/// What <see cref="CfbdCalendarNormalization.NormalizeSeason"/> made of one season's calendar.
/// </summary>
/// <param name="Weeks">
/// The rows to store, in payload order, with the highest week already flagged as championship
/// week (<see cref="SeasonWeek.IsRegularSeason"/> <c>false</c>).
/// </param>
/// <param name="SkippedNonRegular">
/// How many rows were dropped for not being regular-season weeks - CFBD returns exactly one
/// postseason row per season, covering bowls and the playoff, which this product never plays.
/// </param>
/// <param name="DuplicateWeeks">
/// Week numbers a later row repeated and which were therefore ignored. Empty for every real
/// payload once the postseason row (always numbered 1) is gone; kept as a defensive report
/// because a repeat is what made EF throw on the first live run.
/// </param>
public sealed record CalendarNormalizationResult(
    IReadOnlyList<SeasonWeek> Weeks,
    int SkippedNonRegular,
    IReadOnlyList<int> DuplicateWeeks);
