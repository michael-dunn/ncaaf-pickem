using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays ESPN: puts scoreboard lines through <see cref="SimScoreFeed"/> and
/// <see cref="SaturdayPoller.PollOnceAsync"/>, so <c>LiveScoreApplyService</c> applies them
/// with every rule it has - games only move forwards, <c>GameWentFinal</c> fires once and scores
/// the week, <c>GameScheduleChanged</c> pulls a postponed game out of an open set.
/// </summary>
/// <remarks>
/// The one thing the real feed never does is walk a game backwards, and the apply service
/// refuses to. <see cref="ForceAsync"/> is the tester's escape hatch for that (reopen a final
/// game, put a started game back to scheduled): a direct write followed by the same full rescore
/// a correction triggers.
/// </remarks>
public sealed class ScoreDesk
{
    private readonly AppDbContext _database;
    private readonly SimScoreFeed _feed;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly ScoringService _scoring;
    private readonly ILogger<ScoreDesk> _logger;

    /// <summary>Creates the desk. Every dependency is the step's own scope.</summary>
    public ScoreDesk(
        AppDbContext database,
        SimScoreFeed feed,
        IServiceProvider services,
        TimeProvider timeProvider,
        ScoringService scoring,
        ILogger<ScoreDesk> logger)
    {
        _database = database;
        _feed = feed;
        _services = services;
        _timeProvider = timeProvider;
        _scoring = scoring;
        _logger = logger;
    }

    /// <summary>Applies the changes as one poll per Eastern kickoff date.</summary>
    /// <returns>How many games changed, and the domain events that raised.</returns>
    public async Task<(int Changed, int Events)> ApplyAsync(IReadOnlyCollection<ScoreChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return (0, 0);
        }

        Guid[] ids = [.. changes.Select(change => change.GameId).Distinct()];
        Dictionary<Guid, Game> games = await _database.Games
            .AsNoTracking()
            .Include(game => game.HomeTeam)
            .Include(game => game.AwayTeam)
            .Where(game => ids.Contains(game.Id))
            .ToDictionaryAsync(game => game.Id, cancellationToken)
            .ConfigureAwait(false);

        _feed.Queue(changes.Where(change => games.ContainsKey(change.GameId)).Select(change => ToUpdate(games[change.GameId], change)));

        int changed = 0;
        int events = 0;
        foreach (DateOnly easternDate in _feed.PendingDates())
        {
            LiveScoreApplyResult result = await SaturdayPoller
                .PollOnceAsync(_services, _database, _timeProvider, easternDate, _logger, cancellationToken)
                .ConfigureAwait(false);

            changed += result.Changed;
            events += result.Events.Count;
        }

        return (changed, events);
    }

    /// <summary>
    /// Writes a game's status and score directly - including backwards, which the feed cannot -
    /// then rescores every locked week holding it.
    /// </summary>
    public async Task ForceAsync(ScoreChange change, CancellationToken cancellationToken)
    {
        Game game = await _database.Games.FirstAsync(candidate => candidate.Id == change.GameId, cancellationToken).ConfigureAwait(false);

        bool live = change.Status == GameStatus.InProgress;
        game.Status = change.Status;
        game.HomeScore = change.Status == GameStatus.Scheduled ? null : change.Home;
        game.AwayScore = change.Status == GameStatus.Scheduled ? null : change.Away;
        game.Period = live ? change.Period : null;
        game.Clock = live ? change.Clock : null;
        game.LastScoreUpdateUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _scoring.RescoreGameAsync(game.Id, cancellationToken).ConfigureAwait(false);
    }

    private static LiveScoreUpdate ToUpdate(Game game, ScoreChange change) => new(
        SourceEventId: game.CfbdGameId.ToString(CultureInfo.InvariantCulture),
        KickoffUtc: game.KickoffUtc,
        HomeName: game.HomeTeam?.School ?? string.Empty,
        HomeAbbreviation: game.HomeTeam?.Abbreviation ?? string.Empty,
        HomeSourceTeamId: null,
        HomeScore: change.Home,
        AwayName: game.AwayTeam?.School ?? string.Empty,
        AwayAbbreviation: game.AwayTeam?.Abbreviation ?? string.Empty,
        AwaySourceTeamId: null,
        AwayScore: change.Away,
        Status: change.Status,
        RawStatusName: "SIMULATOR_" + change.Status.ToString().ToUpperInvariant(),
        Completed: change.Status == GameStatus.Final,
        Period: change.Period,
        Clock: change.Clock,
        Spread: null,
        Source: ProviderSource.Cfbd);
}
