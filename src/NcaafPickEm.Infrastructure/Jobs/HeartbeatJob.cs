using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Proof of life: a line in the log and a <c>JobRuns</c> row every five minutes, so "are the jobs
/// running?" is answerable from the log file or <c>/api/admin/data-status</c> without waiting for
/// a real job's schedule to come round.
/// </summary>
/// <remarks>Writes nothing but its own <c>JobRuns</c> row; it owns no table.</remarks>
public sealed class HeartbeatJob : IScheduledJob
{
    private readonly ILogger<HeartbeatJob> _logger;

    /// <summary>Creates the job.</summary>
    /// <param name="logger">Structured log sink.</param>
    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Heartbeat";

    /// <inheritdoc />
    public string CronExpression => "*/5 * * * *";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job scheduler heartbeat for occurrence {ScheduledFor:o}", scheduledFor);
        return Task.CompletedTask;
    }
}
