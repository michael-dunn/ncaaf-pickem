using Microsoft.Extensions.Logging;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Weekly teams and conferences refresh (Feature 09). Tuesday morning, after the CFBD roster
/// (transfers, new hires) has usually settled for the week.
/// </summary>
public sealed class TeamsRefreshJob : IScheduledJob
{
    private readonly ReferenceDataIngestService _ingest;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TeamsRefreshJob> _logger;

    /// <summary>Creates the job.</summary>
    public TeamsRefreshJob(
        ReferenceDataIngestService ingest,
        TimeProvider timeProvider,
        ILogger<TeamsRefreshJob> logger)
    {
        _ingest = ingest;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TeamsRefresh";

    /// <inheritdoc />
    public string CronExpression => "0 3 * * 2";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        int season = RefreshJobSupport.CurrentSeasonYear(_timeProvider.GetUtcNow());

        TeamsIngestResult result = await _ingest.IngestTeamsAsync(season, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation(
                "Teams refresh for {Season}: {Conferences} conferences, {Teams} teams, {Aliases} aliases",
                season,
                result.Conferences,
                result.Teams,
                result.Aliases);
        }
        else
        {
            // Already recorded in DataRefreshStatus by the ingest; a job failure here would be
            // double-reporting the same problem, so this is a warning, not a thrown exception.
            _logger.LogWarning("Teams refresh for {Season} failed: {Error}", season, result.Error);
        }
    }
}
