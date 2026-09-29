using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Empties the simulator's database back to "just the players": every league, week, game, poll,
/// line, calendar row and operational row goes, in foreign-key order. Users and their push
/// subscriptions stay, so a browser that subscribed to notifications keeps working and the next
/// sandbox reuses the same players (and their dev-login names).
/// </summary>
public sealed class WipeDesk
{
    private readonly AppDbContext _database;

    /// <summary>Creates the desk.</summary>
    public WipeDesk(AppDbContext database)
    {
        _database = database;
    }

    /// <summary>Deletes everything but users and push subscriptions.</summary>
    public async Task WipeAsync(CancellationToken cancellationToken)
    {
        await _database.PushRetries.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.NotificationLog.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.AuditLog.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.SeasonStandingsSnapshots.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekResults.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Picks.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekSubmissions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekGameSetGames.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.WeekGameSets.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.PointRules.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.GameSetRules.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Invites.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Memberships.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Leagues.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.GameLines.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Rankings.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.UnmatchedGames.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Games.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.SeasonWeeks.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.DataRefreshStatuses.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.JobRuns.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.ProviderCalls.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
