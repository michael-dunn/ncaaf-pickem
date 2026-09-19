using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// The work behind every rankings refresh occurrence (Sunday and Monday evenings, and early
/// Tuesday morning): refresh the AP poll for whichever week is current now (Feature 09).
/// </summary>
public sealed class RankingsRefreshRunner
{
    private readonly ReferenceDataIngestService _ingest;
    private readonly ISeasonWeekSource _weekSource;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RankingsRefreshRunner> _logger;

    /// <summary>Creates the runner.</summary>
    public RankingsRefreshRunner(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        TimeProvider timeProvider,
        ILogger<RankingsRefreshRunner> logger)
    {
        _ingest = ingest;
        _weekSource = weekSource;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the rankings refresh for whichever season and week are current now.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        int season = RefreshJobSupport.CurrentSeasonYear(nowUtc);

        int? currentWeek = await RefreshJobSupport
            .CurrentWeekAsync(_weekSource, season, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (currentWeek is not int week)
        {
            _logger.LogWarning("Rankings refresh for {Season} skipped: no calendar on file yet", season);
            return;
        }

        RankingsIngestResult result = await _ingest
            .IngestRankingsAsync(season, week, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation(
                "Rankings refresh for {Season} week {Week}: {Rankings} rankings",
                season,
                week,
                result.Rankings);
        }
        else
        {
            _logger.LogWarning("Rankings refresh for {Season} week {Week} failed: {Error}", season, week, result.Error);
        }
    }
}
