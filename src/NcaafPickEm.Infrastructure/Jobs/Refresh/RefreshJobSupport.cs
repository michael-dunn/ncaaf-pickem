using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Shared "what season and week is it" resolution for the refresh jobs. No global "current
/// season" configuration exists yet (each <c>League</c> carries its own <c>SeasonYear</c>), so a
/// refresh job resolves the season a college football season year is conventionally named after:
/// the year the season starts in. A season runs from late August through the January bowl games,
/// so a job ticking in January or February still means last year's season.
/// </summary>
public static class RefreshJobSupport
{
    /// <summary>Calendar month (Eastern) a new season year starts being current.</summary>
    private const int SeasonStartMonth = 7;

    /// <summary>The season year to refresh against, given the instant a job ran.</summary>
    /// <param name="nowUtc">The job's scheduled occurrence, in UTC.</param>
    public static int CurrentSeasonYear(DateTimeOffset nowUtc)
    {
        DateTimeOffset eastern = SeasonCalendar.ToEastern(nowUtc);
        return eastern.Month >= SeasonStartMonth ? eastern.Year : eastern.Year - 1;
    }

    /// <summary>
    /// The provider week that is current for <paramref name="season"/>, clamped to the regular
    /// season, or null when the source has no calendar for that season yet.
    /// </summary>
    public static async Task<int?> CurrentWeekAsync(
        ISeasonWeekSource weekSource,
        int season,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> weeks = await weekSource
            .GetWeeksAsync(season, cancellationToken)
            .ConfigureAwait(false);

        return weeks.Count == 0 ? null : SeasonCalendar.CurrentWeekAt(nowUtc, weeks).Week;
    }

    /// <summary>The highest regular-season week on file for <paramref name="season"/>, or null.</summary>
    public static async Task<int?> LastRegularSeasonWeekAsync(
        ISeasonWeekSource weekSource,
        int season,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> weeks = await weekSource
            .GetWeeksAsync(season, cancellationToken)
            .ConfigureAwait(false);

        return weeks.Count == 0 ? null : weeks.Where(week => week.IsRegularSeason).Max(week => week.Week);
    }
}
