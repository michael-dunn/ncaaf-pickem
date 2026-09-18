using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A one-shot source whose due list is whatever the test puts in it, recording every occurrence
/// the scheduler actually ran.
/// </summary>
/// <remarks>
/// Stands in for P4-02's lock job and P7-03's Saturday reminder, which read their due times from
/// <c>WeekGameSets</c> instead of from a list.
/// </remarks>
public sealed class RecordingOneShotJob : IOneShotJob
{
    private readonly List<OneShotOccurrence> _ran = [];
    private readonly Lock _gate = new();

    /// <summary>Creates the source.</summary>
    /// <param name="name">Unique job name; use <see cref="SchedulerHarness.UniqueJobName"/>.</param>
    public RecordingOneShotJob(string name)
    {
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>What <see cref="GetDueAsync"/> answers with. The scheduler filters out the future ones.</summary>
    public List<OneShotOccurrence> Due { get; } = [];

    /// <summary>How many times the scheduler asked what was due.</summary>
    public int Polls { get; private set; }

    /// <summary>Every occurrence the scheduler invoked the job for, in call order.</summary>
    public IReadOnlyList<OneShotOccurrence> Ran
    {
        get
        {
            lock (_gate)
            {
                return [.. _ran];
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        Polls++;
        return Task.FromResult<IReadOnlyList<OneShotOccurrence>>([.. Due]);
    }

    /// <inheritdoc />
    public Task RunAsync(OneShotOccurrence occurrence, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ran.Add(occurrence);
        }

        return Task.CompletedTask;
    }
}
