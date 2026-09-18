using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Writes a live-score snapshot onto the <c>Games</c> table and raises the domain events that
/// follow from it (Feature 09, 04-Domain-Algorithms.md section 9). The Saturday poller (P2-04)
/// fetches the updates; this decides what they mean.
/// </summary>
/// <remarks>
/// Safe to re-run: applying the same snapshot twice changes no row and raises no event. That is
/// what makes the poller's five-minute cadence, the catch-up window and a manual refresh all
/// harmless.
/// </remarks>
public sealed class LiveScoreApplyService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _database;
    private readonly DomainEventCollector _collector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LiveScoreApplyService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="database">The database.</param>
    /// <param name="collector">Collects events raised during the run.</param>
    /// <param name="dispatcher">Delivers them after the save.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Logger.</param>
    public LiveScoreApplyService(
        AppDbContext database,
        DomainEventCollector collector,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<LiveScoreApplyService> logger)
    {
        _database = database;
        _collector = collector;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Applies one snapshot of a day's scores.
    /// </summary>
    /// <param name="easternDate">The Eastern calendar date the snapshot was requested for.</param>
    /// <param name="updates">Everything the provider returned for that date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Candidate games are those kicking off on <paramref name="easternDate"/> <em>or the day
    /// before</em>. ESPN buckets its own payload by Eastern date, so a Saturday call already
    /// contains the 22:30 ET game that goes final at 01:45 ET on Sunday; the extra day exists for
    /// the caller that asks on Sunday instead - a poller tick after midnight, a manual refresh, or
    /// the CFBD fallback, whose week query has no Eastern-day bucketing at all. Two adjacent days
    /// cannot make the matcher ambiguous, because no team plays twice in two days.
    /// </remarks>
    public async Task<LiveScoreApplyResult> ApplyAsync(
        DateOnly easternDate,
        IReadOnlyList<LiveScoreUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        DateOnly previousDate = easternDate.AddDays(-1);

        List<Game> candidates = await _database.Games
            .Where(game => game.KickoffEasternDate == easternDate || game.KickoffEasternDate == previousDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No games scheduled on {EasternDate} (or the day before); nothing to apply", easternDate);
            return new LiveScoreApplyResult(0, 0, 0, updates.Count, []);
        }

        List<Team> teams = await _database.Teams.ToListAsync(cancellationToken).ConfigureAwait(false);
        List<TeamAlias> aliases = await _database.TeamAliases.ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, Team> teamsById = teams.ToDictionary(team => team.Id);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        int matched = 0;
        int changed = 0;
        int unmatched = 0;
        int ignored = 0;
        HashSet<string> unmatchedWritten = new(StringComparer.Ordinal);

        foreach (IGrouping<ProviderSource, LiveScoreUpdate> bySource in updates.GroupBy(update => update.Source))
        {
            var matcher = new GameMatcher(TeamNameIndex.Build(teams, aliases, bySource.Key), candidates);

            foreach (LiveScoreUpdate update in bySource)
            {
                GameMatchResult match = matcher.Match(update);

                if (match.Game is null)
                {
                    switch (match.Outcome)
                    {
                        case GameMatchOutcome.Ignored:
                            ignored++;
                            break;

                        default:
                            if (await RecordUnmatchedAsync(update, bySource.Key, match, nowUtc, unmatchedWritten, cancellationToken)
                                .ConfigureAwait(false))
                            {
                                unmatched++;
                            }

                            break;
                    }

                    continue;
                }

                matched++;
                if (ApplyToGame(match.Game, update, match.SidesSwapped, teamsById, nowUtc))
                {
                    changed++;
                }
            }
        }

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied {UpdateCount} live updates for {EasternDate}: {Matched} matched, {Changed} changed, " +
            "{Unmatched} unmatched, {Ignored} ignored, {EventCount} events",
            updates.Count,
            easternDate,
            matched,
            changed,
            unmatched,
            ignored,
            raised.Count);

        return new LiveScoreApplyResult(matched, changed, unmatched, ignored, raised);
    }

    private bool ApplyToGame(
        Game game,
        LiveScoreUpdate update,
        bool sidesSwapped,
        IReadOnlyDictionary<Guid, Team> teamsById,
        DateTime nowUtc)
    {
        bool changed = LearnProviderIds(game, update, sidesSwapped, teamsById);

        GameStatus previousStatus = game.Status;
        if (!IsAcceptableTransition(previousStatus, update.Status))
        {
            // A stale or contradictory payload. Never walk a Final game backwards: scoring has
            // already run against it (04-Domain-Algorithms.md section 7).
            _logger.LogDebug(
                "Ignoring {NewStatus} for game {GameId}, which is already {CurrentStatus}",
                update.Status,
                game.Id,
                previousStatus);
            return changed;
        }

        if (game.Status != update.Status)
        {
            game.Status = update.Status;
            changed = true;
        }

        changed |= ApplyScores(game, update, sidesSwapped);
        changed |= ApplyClock(game, update);

        if (changed)
        {
            game.LastScoreUpdateUtc = nowUtc;
        }

        RaiseTransitionEvents(game, previousStatus, nowUtc);
        return changed;
    }

    private static bool ApplyScores(Game game, LiveScoreUpdate update, bool sidesSwapped)
    {
        if (update.Status == GameStatus.Scheduled)
        {
            // Before kickoff the provider's "0-0" is a placeholder, not a score (D-012).
            return false;
        }

        int? home = sidesSwapped ? update.AwayScore : update.HomeScore;
        int? away = sidesSwapped ? update.HomeScore : update.AwayScore;
        bool changed = false;

        if (home is not null && game.HomeScore != home)
        {
            game.HomeScore = home;
            changed = true;
        }

        if (away is not null && game.AwayScore != away)
        {
            game.AwayScore = away;
            changed = true;
        }

        return changed;
    }

    private static bool ApplyClock(Game game, LiveScoreUpdate update)
    {
        // Period and clock only mean something while the game is running; a finished game that
        // still says "4th, 0:00" reads as live on the dashboard.
        byte? period = update.Status == GameStatus.InProgress ? update.Period : null;
        string? clock = update.Status == GameStatus.InProgress ? Truncate(update.Clock, Game.ClockMaxLength) : null;

        bool changed = false;

        if (game.Period != period)
        {
            game.Period = period;
            changed = true;
        }

        if (!string.Equals(game.Clock, clock, StringComparison.Ordinal))
        {
            game.Clock = clock;
            changed = true;
        }

        return changed;
    }

    private static bool LearnProviderIds(
        Game game,
        LiveScoreUpdate update,
        bool sidesSwapped,
        IReadOnlyDictionary<Guid, Team> teamsById)
    {
        if (update.Source != ProviderSource.Espn)
        {
            return false;
        }

        bool changed = false;

        if (game.EspnEventId is null
            && long.TryParse(update.SourceEventId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long eventId))
        {
            game.EspnEventId = eventId;
            changed = true;
        }

        string? homeSourceId = sidesSwapped ? update.AwaySourceTeamId : update.HomeSourceTeamId;
        string? awaySourceId = sidesSwapped ? update.HomeSourceTeamId : update.AwaySourceTeamId;

        changed |= LearnTeamId(teamsById, game.HomeTeamId, homeSourceId);
        changed |= LearnTeamId(teamsById, game.AwayTeamId, awaySourceId);

        return changed;
    }

    private static bool LearnTeamId(IReadOnlyDictionary<Guid, Team> teamsById, Guid teamId, string? sourceTeamId)
    {
        if (!teamsById.TryGetValue(teamId, out Team? team)
            || team.EspnTeamId is not null
            || !int.TryParse(sourceTeamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int espnTeamId))
        {
            return false;
        }

        team.EspnTeamId = espnTeamId;
        return true;
    }

    private void RaiseTransitionEvents(Game game, GameStatus previousStatus, DateTime nowUtc)
    {
        if (game.Status == previousStatus)
        {
            return;
        }

        if (game.Status == GameStatus.Final)
        {
            _collector.Raise(new GameWentFinal(game.Id, game.SeasonYear, game.Week, DetermineWinner(game))
            {
                OccurredUtc = nowUtc,
            });
        }

        if (IsDisrupted(game.Status) || IsDisrupted(previousStatus))
        {
            _collector.Raise(new GameScheduleChanged(game.Id, previousStatus, game.Status)
            {
                OccurredUtc = nowUtc,
            });
        }
    }

    /// <summary>
    /// The winning team, or null when the feed reported a tie or no scores - which is the
    /// "needs review" path in 04-Domain-Algorithms.md section 7, not a win for anybody.
    /// </summary>
    private static Guid? DetermineWinner(Game game)
    {
        if (game.HomeScore is not int home || game.AwayScore is not int away || home == away)
        {
            return null;
        }

        return home > away ? game.HomeTeamId : game.AwayTeamId;
    }

    private static bool IsDisrupted(GameStatus status) =>
        status is GameStatus.Postponed or GameStatus.Cancelled;

    /// <summary>
    /// Games move forwards, not backwards. Postponement and cancellation can happen at any point
    /// (and can be undone), but nothing walks a Final game back to InProgress or a running game
    /// back to Scheduled on the strength of one stale payload.
    /// </summary>
    private static bool IsAcceptableTransition(GameStatus current, GameStatus next) => next switch
    {
        _ when next == current => true,
        GameStatus.Postponed or GameStatus.Cancelled => true,
        _ when IsDisrupted(current) => true,
        GameStatus.Final => current != GameStatus.Final,
        GameStatus.InProgress => current == GameStatus.Scheduled,
        _ => false,
    };

    private async Task<bool> RecordUnmatchedAsync(
        LiveScoreUpdate update,
        ProviderSource source,
        GameMatchResult match,
        DateTime nowUtc,
        HashSet<string> alreadyWritten,
        CancellationToken cancellationToken)
    {
        string rawHome = Truncate(update.HomeName, UnmatchedGame.RawNameMaxLength) ?? string.Empty;
        string rawAway = Truncate(update.AwayName, UnmatchedGame.RawNameMaxLength) ?? string.Empty;
        DateOnly gameDate = KickoffEasternDate(update.KickoffUtc);

        if (!alreadyWritten.Add($"{source}|{rawHome}|{rawAway}|{gameDate:yyyy-MM-dd}"))
        {
            return false;
        }

        bool exists = await _database.UnmatchedGames
            .AnyAsync(
                row => row.Source == source
                    && row.RawHomeName == rawHome
                    && row.RawAwayName == rawAway
                    && row.GameDate == gameDate,
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return false;
        }

        _database.UnmatchedGames.Add(new UnmatchedGame
        {
            Id = Guid.CreateVersion7(),
            Source = source,
            RawHomeName = rawHome,
            RawAwayName = rawAway,
            GameDate = gameDate,
            RawPayload = JsonSerializer.Serialize(update, PayloadOptions),
            FirstSeenUtc = nowUtc,
        });

        _logger.LogWarning(
            "Unmatched {Source} game {Away} at {Home} on {GameDate}: {Reason}",
            source,
            rawAway,
            rawHome,
            gameDate,
            match.Reason);

        return true;
    }

    private static DateOnly KickoffEasternDate(DateTime kickoffUtc)
    {
        DateTimeOffset utc = new(DateTime.SpecifyKind(kickoffUtc, DateTimeKind.Utc));
        return DateOnly.FromDateTime(SeasonCalendar.ToEastern(utc).DateTime);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
