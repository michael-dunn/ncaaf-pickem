using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// <see cref="ISeasonWeekSource"/> over the <c>SeasonWeeks</c> rows
/// <c>ReferenceDataIngestService</c> (via <see cref="CfbdCalendarNormalization"/>) has ingested.
/// Registered instead of
/// <see cref="Fixture.FixtureSeasonWeekSource"/> when <c>Providers:ReferenceData</c> is
/// <c>Cfbd</c>.
/// </summary>
/// <remarks>
/// Cached per season for a few minutes: every page that renders a week window
/// (<c>04-Domain-Algorithms.md</c> section 1) calls this, and the calendar changes at most once a
/// week. The cache is process-static rather than per-instance because this type is registered
/// scoped (it takes a scoped <see cref="AppDbContext"/>), so an instance-level cache would never
/// survive past the request that created it.
/// </remarks>
public sealed class DbSeasonWeekSource : ISeasonWeekSource
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<int, (DateTime ExpiresUtc, IReadOnlyList<SeasonWeek> Weeks)> Cache = [];

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the source.</summary>
    public DbSeasonWeekSource(AppDbContext database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SeasonWeek>> GetWeeksAsync(
        int seasonYear,
        CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(seasonYear, out var entry) && entry.ExpiresUtc > nowUtc)
            {
                return entry.Weeks;
            }
        }

        List<SeasonWeek> weeks = await _database.SeasonWeeks
            .Where(week => week.SeasonYear == seasonYear)
            .OrderBy(week => week.Week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SeasonWeek> result = weeks;

        lock (CacheLock)
        {
            Cache[seasonYear] = (nowUtc + CacheDuration, result);
        }

        return result;
    }
}
