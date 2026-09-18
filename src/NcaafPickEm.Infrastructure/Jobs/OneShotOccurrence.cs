namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// One thing an <see cref="IOneShotJob"/> has to do once, at one moment.
/// </summary>
/// <param name="Key">
/// Identifies the occurrence within its job, for example a <c>WeekGameSets.Id</c>. Combined with
/// the job name it forms the <c>JobRuns.JobName</c> value that makes the run idempotent, so it
/// must be stable across ticks and short enough to fit (see <see cref="IOneShotJob"/>).
/// </param>
/// <param name="DueUtc">
/// When the work became due, in UTC. Stored as <c>JobRuns.ScheduledForUtc</c>, so moving a due
/// time — a commissioner changing a lock time, say — produces a genuinely new occurrence that
/// runs again rather than being deduplicated against the old one.
/// </param>
public readonly record struct OneShotOccurrence(string Key, DateTimeOffset DueUtc);
