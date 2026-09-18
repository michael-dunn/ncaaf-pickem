using NcaafPickEm.Shared.Contracts.Seasons;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="ISeasonsApi"/> matching the shape of D-026's 2026 fixture calendar
/// (weeks 0-14 regular season, week 15 championship week), so week pickers work without P2-02's
/// real calendar ingest.
/// </summary>
public sealed class FakeSeasonsApi : ISeasonsApi
{
    private static readonly DateOnly Week1Saturday = new(2026, 9, 5);

    /// <inheritdoc />
    public Task<SeasonWeek[]> GetWeeksAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year != 2026)
        {
            throw new LeaguesApiException(404, $"No season calendar for {year}.");
        }

        SeasonWeek[] weeks = Enumerable.Range(0, 16).Select(BuildWeek).ToArray();
        return Task.FromResult(weeks);
    }

    private static SeasonWeek BuildWeek(int week)
    {
        DateOnly saturday = Week1Saturday.AddDays((week - 1) * 7);
        DateOnly sunday = saturday.AddDays(-6);
        var startUtc = new DateTimeOffset(sunday.Year, sunday.Month, sunday.Day, 0, 0, 0, TimeSpan.Zero);
        var endUtc = new DateTimeOffset(saturday.Year, saturday.Month, saturday.Day, 23, 59, 59, TimeSpan.Zero)
            .AddMilliseconds(999);
        bool isRegularSeason = week is >= 0 and <= 14;
        return new SeasonWeek(week, startUtc, endUtc, isRegularSeason);
    }
}
