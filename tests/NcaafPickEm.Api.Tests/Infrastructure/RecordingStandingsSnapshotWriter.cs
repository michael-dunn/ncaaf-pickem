using NcaafPickEm.Infrastructure.Scoring;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="IStandingsSnapshotWriter"/> that records the calls instead of writing rows, so a
/// test can assert that a week's completion triggered exactly one snapshot without depending on
/// P5-03's calculator.
/// </summary>
/// <remarks>
/// Register it as a singleton through <c>ApiFactory</c>'s <c>configureServices</c> hook after a
/// <c>RemoveAll&lt;IStandingsSnapshotWriter&gt;()</c>; the app's own registration is a
/// <c>TryAddScoped</c> that a plain <c>TryAdd</c> here would lose to.
/// </remarks>
public sealed class RecordingStandingsSnapshotWriter : IStandingsSnapshotWriter
{
    private readonly List<(Guid LeagueId, int ThroughWeek)> _calls = [];
    private readonly Lock _gate = new();

    /// <summary>Every snapshot asked for, in order.</summary>
    public IReadOnlyList<(Guid LeagueId, int ThroughWeek)> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>How many snapshots were asked for, for one league's week.</summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="throughWeek">The week.</param>
    public int CountFor(Guid leagueId, int throughWeek)
    {
        lock (_gate)
        {
            return _calls.Count(call => call.LeagueId == leagueId && call.ThroughWeek == throughWeek);
        }
    }

    /// <inheritdoc />
    public Task WriteSnapshotAsync(Guid leagueId, int throughWeek, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _calls.Add((leagueId, throughWeek));
        }

        return Task.CompletedTask;
    }
}
