using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers.Cfbd;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays CFBD's calendar feed: replaces the simulated season's <c>SeasonWeeks</c> with a layout
/// from <see cref="SimCalendarLayout"/>, and keeps each league's stored completion flag in step
/// with it, since the jobs filter on that flag (D-185).
/// </summary>
public sealed class CalendarDesk
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the desk.</summary>
    public CalendarDesk(AppDbContext database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <summary>The season's weeks as stored, ordered; empty before the first layout.</summary>
    public async Task<IReadOnlyList<SeasonWeek>> ReadAsync(int seasonYear, CancellationToken cancellationToken) =>
        await _database.SeasonWeeks
            .AsNoTracking()
            .Where(week => week.SeasonYear == seasonYear)
            .OrderBy(week => week.Week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The week the app considers current right now, and why - exactly
    /// <see cref="SeasonCalendar.CurrentWeekAt"/> over the stored weeks - or null with no calendar.
    /// </summary>
    public async Task<CurrentWeek?> CurrentAsync(int seasonYear, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> weeks = await ReadAsync(seasonYear, cancellationToken).ConfigureAwait(false);
        return weeks.Count == 0 ? null : SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), weeks);
    }

    /// <summary>Replaces the season's weeks and re-syncs every league's completion flag.</summary>
    public async Task WriteAsync(int seasonYear, IReadOnlyList<SeasonWeek> weeks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weeks);

        await _database.SeasonWeeks
            .Where(week => week.SeasonYear == seasonYear)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _database.SeasonWeeks.AddRange(weeks);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        List<League> leagues = await _database.Leagues
            .Where(league => league.SeasonYear == seasonYear)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (League league in leagues)
        {
            league.IsComplete = weeks.Any(week => week.Week == league.LastWeek)
                && SeasonCalendar.LeagueIsCompleteAt(nowUtc, league.LastWeek, weeks);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // This process's own cache; the app's expires within DbSeasonWeekSource.SimulatorCacheDuration.
        DbSeasonWeekSource.Invalidate(seasonYear);
    }
}
