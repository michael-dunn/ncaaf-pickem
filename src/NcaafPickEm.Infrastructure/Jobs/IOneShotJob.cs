namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// A job whose schedule lives in the database rather than in a cron string: the week lock
/// (P4-02) and the Saturday reminder (P7-03).
/// </summary>
/// <remarks>
/// This is the P0-06 card's <c>IOneShotScheduler</c>, named for what an implementation actually
/// is. Nothing is queued and no timer is held in memory: <see cref="SchedulerTick"/> asks every
/// registered source what is due on every tick, so a due time that moves between ticks is simply
/// read again and honoured. Register implementations with <c>AddOneShotJob&lt;T&gt;()</c>.
/// <para>
/// The scheduler deduplicates a run as <c>JobRuns(JobName = "{Name}:{Key}", ScheduledForUtc =
/// DueUtc)</c>, so the name, a colon, and the key together must fit in
/// <see cref="Domain.Operations.JobRun.JobNameMaxLength"/> characters. A longer pair is logged as
/// an error and skipped rather than silently truncated into a collision.
/// </para>
/// </remarks>
public interface IOneShotJob
{
    /// <summary>Stable identifier used as the prefix of <c>JobRuns.JobName</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Reads the target times out of the database and returns the occurrences that are due.
    /// </summary>
    /// <param name="nowUtc">The tick's clock reading.</param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    /// <returns>Every outstanding occurrence; an empty list when there is nothing to do.</returns>
    /// <remarks>
    /// The scheduler drops occurrences dated after <paramref name="nowUtc"/> and skips ones it has
    /// already recorded, so a source is free to answer the simple query (for example
    /// <c>LockAtUtc &lt;= now AND LockedUtc IS NULL</c>) without tracking state of its own.
    /// </remarks>
    Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Does the work for one occurrence, in its own DI scope.</summary>
    /// <param name="occurrence">The occurrence, exactly as <see cref="GetDueAsync"/> returned it.</param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    Task RunAsync(OneShotOccurrence occurrence, CancellationToken cancellationToken);
}
