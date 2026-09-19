using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Scoring;

/// <summary>
/// The fallback <see cref="IStandingsSnapshotWriter"/>: logs that a snapshot was due and writes
/// nothing. In place only while P5-03's <c>StandingsCalculator</c>-backed writer is not
/// registered.
/// </summary>
/// <remarks>
/// Deliberately not a silent no-op. A missing trend arrow on the season leaderboard is the kind
/// of thing nobody notices for weeks, so the one log line is the difference between "the
/// standings writer is not wired up" and an unexplained gap in
/// <c>SeasonStandingsSnapshots</c>.
/// </remarks>
public sealed class NoOpStandingsSnapshotWriter : IStandingsSnapshotWriter
{
    private readonly ILogger<NoOpStandingsSnapshotWriter> _logger;

    /// <summary>Creates the writer.</summary>
    /// <param name="logger">Structured log sink.</param>
    public NoOpStandingsSnapshotWriter(ILogger<NoOpStandingsSnapshotWriter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteSnapshotAsync(Guid leagueId, int throughWeek, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "League {LeagueId} week {ThroughWeek} became complete, but no standings snapshot writer "
            + "is registered; SeasonStandingsSnapshots was not written.",
            leagueId,
            throughWeek);

        return Task.CompletedTask;
    }
}
