namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// A job that runs on a cron schedule evaluated in Eastern time (D-005).
/// </summary>
/// <remarks>
/// Register implementations with <c>AddScheduledJob&lt;T&gt;()</c>. <see cref="SchedulerTick"/>
/// resolves them in a fresh DI scope for every occurrence, so an implementation may take scoped
/// services such as <c>AppDbContext</c>.
/// <para>
/// Every implementation must be idempotent. The scheduler guarantees at most one run per
/// <c>(Name, occurrence)</c> pair through the <c>JobRuns</c> unique index, but a process that
/// dies mid-run leaves the row claimed with the work half done, and the catch-up window replays
/// an occurrence that was missed during an outage.
/// </para>
/// </remarks>
public interface IScheduledJob
{
    /// <summary>
    /// Stable identifier, written to <c>JobRuns.JobName</c>. Must be unique across all registered
    /// jobs and at most <see cref="Domain.Operations.JobRun.JobNameMaxLength"/> characters.
    /// </summary>
    /// <remarks>Renaming a job resets its history, so its catch-up window starts from scratch.</remarks>
    string Name { get; }

    /// <summary>
    /// A standard five-field cron expression (minute, hour, day of month, month, day of week),
    /// evaluated in <c>America/New_York</c>, never in UTC.
    /// </summary>
    string CronExpression { get; }

    /// <summary>Does the work for one occurrence.</summary>
    /// <param name="scheduledFor">
    /// The cron occurrence this run belongs to, in UTC. Not the moment the run actually started:
    /// after a restart it can be minutes in the past.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken);
}
