using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Nightly betting-lines refresh, late enough to have picked up the day's line movement
/// (Feature 09). Lines are appended, never overwritten (<c>GameLines</c> keeps history), so a
/// nightly cadence is safe even mid-week.
/// </summary>
public sealed class LinesRefreshJob : IScheduledJob
{
    private readonly ReferenceDataIngestService _ingest;
    private readonly ISeasonWeekSource _weekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LinesRefreshJob> _logger;

    /// <summary>Creates the job.</summary>
    public LinesRefreshJob(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        TimeProvider timeProvider,
        ILogger<LinesRefreshJob> logger)
    {
        _ingest = ingest;
        _weekSource = weekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "LinesRefresh";

    /// <inheritdoc />
    public string CronExpression => "30 23 * * *";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        int season = RefreshJobSupport.CurrentSeasonYear(nowUtc);

        int? currentWeek = await RefreshJobSupport
            .CurrentWeekAsync(_weekSource, season, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (currentWeek is not int week)
        {
            _logger.LogWarning("Lines refresh for {Season} skipped: no calendar on file yet", season);
            return;
        }

        LinesIngestResult result = await _ingest
            .IngestLinesAsync(season, week, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation("Lines refresh for {Season} week {Week}: {Lines} lines", season, week, result.Lines);
        }
        else
        {
            _logger.LogWarning("Lines refresh for {Season} week {Week} failed: {Error}", season, week, result.Error);
        }
    }
}
