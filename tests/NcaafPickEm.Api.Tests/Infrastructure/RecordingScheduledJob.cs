using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A cron job that records every occurrence it is handed, so a test can assert how many times it
/// ran and for which minute.
/// </summary>
public sealed class RecordingScheduledJob : IScheduledJob
{
    private readonly List<DateTimeOffset> _occurrences = [];
    private readonly Lock _gate = new();

    /// <summary>Creates the job.</summary>
    /// <param name="name">Unique job name; use <see cref="SchedulerHarness.UniqueJobName"/>.</param>
    /// <param name="cronExpression">Eastern cron expression.</param>
    public RecordingScheduledJob(string name, string cronExpression)
    {
        Name = name;
        CronExpression = cronExpression;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string CronExpression { get; }

    /// <summary>Set to throw from <see cref="RunAsync"/>, to test failure recording.</summary>
    public Exception? ThrowOnRun { get; set; }

    /// <summary>Completed the first time the job runs, for tests that await the hosted service.</summary>
    public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every occurrence the scheduler invoked the job for, in call order.</summary>
    public IReadOnlyList<DateTimeOffset> Occurrences
    {
        get
        {
            lock (_gate)
            {
                return [.. _occurrences];
            }
        }
    }

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _occurrences.Add(scheduledFor);
        }

        Ran.TrySetResult();

        return ThrowOnRun is null ? Task.CompletedTask : Task.FromException(ThrowOnRun);
    }
}
