using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Moves a whole simulated week through its lifecycle: locks what is due with the app's own
/// <see cref="LockWeekJob"/>, and takes a week back - to before its lock with the picks kept, or
/// all the way to a blank week - so the lock, the dashboard and scoring can be run again without
/// starting a new season.
/// </summary>
/// <remarks>
/// Rewinding and resetting undo what only the app ever writes (the lock, results, standings
/// snapshots, corrections, the week's notification log), which is why they are explicit deletes
/// and updates here rather than service calls: nothing in the app goes backwards.
/// </remarks>
public sealed class WeekDesk
{
    private readonly AppDbContext _database;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly PickService _picks;

    /// <summary>Creates the desk. Every dependency is the step's own scope.</summary>
    public WeekDesk(AppDbContext database, IServiceProvider services, TimeProvider timeProvider, PickService picks)
    {
        _database = database;
        _services = services;
        _timeProvider = timeProvider;
        _picks = picks;
    }

    /// <summary>
    /// Runs <see cref="LockWeekJob"/> for every week whose lock has passed, as the scheduler's next
    /// tick would - so a lock the simulator just moved to "now" happens now rather than within the
    /// minute.
    /// </summary>
    /// <returns>How many weeks were locked.</returns>
    public async Task<int> LockDueAsync(CancellationToken cancellationToken)
    {
        LockWeekJob job = _services.GetServices<IOneShotJob>().OfType<LockWeekJob>().Single();
        IReadOnlyList<OneShotOccurrence> due = await job.GetDueAsync(_timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        foreach (OneShotOccurrence occurrence in due)
        {
            await job.RunAsync(occurrence, cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }

    /// <summary>
    /// Takes every league's set for the week back to before its lock, keeping the picks: the lock,
    /// the frozen lines, voids and overrides, results, standings from this week on and the week's
    /// notification log go; games go back to Scheduled; each member's status is recomputed from
    /// their picks. Move the kickoffs forward afterwards.
    /// </summary>
    /// <returns>How many sets were rewound.</returns>
    public async Task<int> RewindAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        List<WeekGameSet> sets = await SetsAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        Guid[] setIds = [.. sets.Select(set => set.Id)];
        Guid[] leagueIds = [.. sets.Select(set => set.LeagueId)];

        await ClearOutcomesAsync(setIds, leagueIds, week, cancellationToken).ConfigureAwait(false);

        await _database.WeekGameSetGames
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.SpreadAtLock, (decimal?)null)
                    .SetProperty(row => row.IsVoided, false)
                    .SetProperty(row => row.ResultOverrideWinnerTeamId, (Guid?)null),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (WeekGameSet set in sets)
        {
            set.LockedUtc = null;
            set.IsComplete = false;
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ResetGamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);

        foreach (WeekGameSet set in sets)
        {
            await _picks.RecomputeWeekStatusesAsync(set.Id, null, cancellationToken).ConfigureAwait(false);
        }

        return sets.Count;
    }

    /// <summary>
    /// Wipes every league's week: picks, submissions, the sets themselves, results, standings from
    /// this week on and the notification log, and puts the games back to Scheduled. Regenerate
    /// afterwards.
    /// </summary>
    /// <returns>How many sets were deleted.</returns>
    public async Task<int> ResetAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        List<WeekGameSet> sets = await SetsAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        Guid[] setIds = [.. sets.Select(set => set.Id)];
        Guid[] leagueIds = [.. sets.Select(set => set.LeagueId)];

        await ClearOutcomesAsync(setIds, leagueIds, week, cancellationToken).ConfigureAwait(false);
        await _database.Picks
            .Where(pick => setIds.Contains(pick.WeekGameSetGame!.WeekGameSetId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekSubmissions
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekGameSetGames
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekGameSets
            .Where(set => setIds.Contains(set.Id))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        await ResetGamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        return sets.Count;
    }

    /// <summary>Deletes the week's games and their lines. Only safe once no set refers to them.</summary>
    public async Task<int> DeleteGamesAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        await _database.GameLines
            .Where(line => line.Game!.SeasonYear == seasonYear && line.Game.Week == week)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return await _database.Games
            .Where(game => game.SeasonYear == seasonYear && game.Week == week)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<List<WeekGameSet>> SetsAsync(int seasonYear, int week, CancellationToken cancellationToken) =>
        _database.WeekGameSets
            .Where(set => set.Week == week && set.League!.SeasonYear == seasonYear)
            .ToListAsync(cancellationToken);

    private async Task ClearOutcomesAsync(Guid[] setIds, Guid[] leagueIds, int week, CancellationToken cancellationToken)
    {
        await _database.WeekResults
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.SeasonStandingsSnapshots
            .Where(row => leagueIds.Contains(row.LeagueId) && row.ThroughWeek >= week)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.NotificationLog
            .Where(row => leagueIds.Contains(row.LeagueId) && row.Week == week)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<int> ResetGamesAsync(int seasonYear, int week, CancellationToken cancellationToken) =>
        _database.Games
            .Where(game => game.SeasonYear == seasonYear && game.Week == week)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(game => game.Status, GameStatus.Scheduled)
                    .SetProperty(game => game.HomeScore, (int?)null)
                    .SetProperty(game => game.AwayScore, (int?)null)
                    .SetProperty(game => game.Period, (byte?)null)
                    .SetProperty(game => game.Clock, (string?)null)
                    .SetProperty(game => game.LastScoreUpdateUtc, (DateTime?)null),
                cancellationToken);
}
