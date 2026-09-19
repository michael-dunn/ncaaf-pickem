using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Reacts to <see cref="GameScheduleChanged"/> (Feature 02, P3-04): keeps every league's
/// <c>WeekGameSetGames</c> in sync with a game that entered or left <see cref="GameStatus.Postponed"/>
/// or <see cref="GameStatus.Cancelled"/>.
/// </summary>
/// <remarks>
/// Before a week's lock, a disrupted game is removed from every set that carries it
/// (<see cref="GameRemovedFromSet"/>) and a game that comes back to <see cref="GameStatus.Scheduled"/>
/// is restored, but only when it was this handler that took it out (<c>RemovedReason == "Schedule
/// change"</c>) - a commissioner's own manual removal is left alone. After lock the row is left
/// exactly as it is and <see cref="GameNeedsVoidReview"/> is raised instead, so P5-02's void flow
/// and P2-04's data page can pick it up.
/// <para>
/// Idempotent by construction: <see cref="LiveScoreApplyService"/> only raises
/// <see cref="GameScheduleChanged"/> when a game's status actually changes, so the same real-world
/// transition is never delivered twice, and this handler's own before-lock branches are guarded on
/// the row's current <see cref="WeekGameSetGame.IsRemoved"/>/<see cref="WeekGameSetGame.RemovedReason"/>
/// state, so replaying the same event a second time (a crash between save and dispatch, say) finds
/// nothing left to do.
/// </para>
/// </remarks>
public sealed class ScheduleChangeHandler : IDomainEventHandler<GameScheduleChanged>
{
    /// <summary>The one <c>RemovedReason</c> this handler ever writes or restores from.</summary>
    public const string ScheduleChangeReason = "Schedule change";

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly DomainEventCollector _collector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ILogger<ScheduleChangeHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public ScheduleChangeHandler(
        AppDbContext database,
        TimeProvider timeProvider,
        DomainEventCollector collector,
        IDomainEventDispatcher dispatcher,
        ILogger<ScheduleChangeHandler> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _collector = collector;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameScheduleChanged domainEvent, CancellationToken cancellationToken)
    {
        bool enteredDisrupted = IsDisrupted(domainEvent.NewStatus) && !IsDisrupted(domainEvent.OldStatus);
        bool leftDisrupted = !IsDisrupted(domainEvent.NewStatus) && IsDisrupted(domainEvent.OldStatus);

        if (!enteredDisrupted && !leftDisrupted)
        {
            // Postponed -> Cancelled (or the reverse): still disrupted either way, nothing to
            // reconcile against the game sets.
            return;
        }

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .Include(row => row.WeekGameSet)
            .Include(row => row.Game)
            .Where(row => row.GameId == domainEvent.GameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        bool anyDatabaseChange = false;
        var touchedSets = new Dictionary<Guid, WeekGameSet>();

        if (enteredDisrupted)
        {
            foreach (WeekGameSetGame row in rows.Where(r => !r.IsRemoved))
            {
                WeekGameSet set = row.WeekGameSet!;

                if (set.LockedUtc is null)
                {
                    row.IsRemoved = true;
                    row.RemovedReason = ScheduleChangeReason;
                    touchedSets[set.Id] = set;
                    anyDatabaseChange = true;

                    _collector.Raise(new GameRemovedFromSet(set.LeagueId, set.Week, set.Id, row.Id, row.GameId, ScheduleChangeReason)
                    {
                        OccurredUtc = nowUtc,
                    });

                    _logger.LogInformation(
                        "Game {GameId} removed from league {LeagueId} week {Week} (schedule change: {OldStatus} -> {NewStatus}).",
                        row.GameId,
                        set.LeagueId,
                        set.Week,
                        domainEvent.OldStatus,
                        domainEvent.NewStatus);
                }
                else
                {
                    _collector.Raise(new GameNeedsVoidReview(set.LeagueId, set.Week, set.Id, row.Id, row.GameId, ScheduleChangeReason)
                    {
                        OccurredUtc = nowUtc,
                    });

                    _logger.LogInformation(
                        "Game {GameId} in already-locked league {LeagueId} week {Week} needs a void review (schedule change: {OldStatus} -> {NewStatus}).",
                        row.GameId,
                        set.LeagueId,
                        set.Week,
                        domainEvent.OldStatus,
                        domainEvent.NewStatus);
                }
            }
        }
        else
        {
            foreach (WeekGameSetGame row in rows.Where(r => r.IsRemoved && r.RemovedReason == ScheduleChangeReason))
            {
                WeekGameSet set = row.WeekGameSet!;

                if (set.LockedUtc is not null)
                {
                    // The set was already frozen while the game was disrupted; nothing to
                    // restore. P5-02's void flow (if it ran) owns whatever happens next.
                    continue;
                }

                row.IsRemoved = false;
                row.RemovedReason = null;
                touchedSets[set.Id] = set;
                anyDatabaseChange = true;

                _collector.Raise(new GameAddedToSet(set.LeagueId, set.Week, set.Id, [row.Id], ScheduleChangeReason)
                {
                    OccurredUtc = nowUtc,
                });

                _logger.LogInformation(
                    "Game {GameId} restored to league {LeagueId} week {Week} (schedule change reverted: {OldStatus} -> {NewStatus}).",
                    row.GameId,
                    set.LeagueId,
                    set.Week,
                    domainEvent.OldStatus,
                    domainEvent.NewStatus);
            }
        }

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();

        if (anyDatabaseChange)
        {
            // Save the removed/restored rows first: RecalculateLockAtUtcAsync below queries the
            // database directly (it needs every OTHER row in the set, which this handler never
            // loaded), so it must see today's write before it can recompute today's answer.
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (WeekGameSet set in touchedSets.Values)
            {
                set.LockAtUtc = await RecalculateLockAtUtcAsync(set.Id, cancellationToken).ConfigureAwait(false);
            }

            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (raised.Count > 0)
        {
            await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsDisrupted(GameStatus status) => status is GameStatus.Postponed or GameStatus.Cancelled;

    /// <summary>
    /// Earliest Saturday kickoff among a set's active games (matches
    /// <c>GameSetService</c>'s own rule); null when it has none. Queried fresh rather than from
    /// the rows this handler already loaded, since those cover only the one game the event is
    /// about, not the rest of the set. EF's identity map still returns the in-memory (not yet
    /// saved) state for whichever row we just changed.
    /// </summary>
    private async Task<DateTime?> RecalculateLockAtUtcAsync(Guid weekGameSetId, CancellationToken cancellationToken)
    {
        List<DateTime> kickoffs = await _database.WeekGameSetGames
            .Include(row => row.Game)
            .Where(row => row.WeekGameSetId == weekGameSetId && !row.IsRemoved)
            .Select(row => row.Game!.KickoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return kickoffs.Count == 0 ? null : kickoffs.Min();
    }
}
