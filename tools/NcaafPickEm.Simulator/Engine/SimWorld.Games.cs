using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <content>Games: the slate, kickoffs, lines, the poll, the scoreboard and autoplay.</content>
public sealed partial class SimWorld
{
    private const string QuarterStartClock = "15:00";

    /// <summary>
    /// Adds a hand-picked game to the current week, kicking off <paramref name="minutesAfterFirstKickoff"/>
    /// after the week's first kickoff. It is in no league's set until a commissioner adds it in
    /// the app or a regeneration picks it up.
    /// </summary>
    public Task<SimLog> AddGameAsync(
        Guid homeTeamId,
        Guid awayTeamId,
        int minutesAfterFirstKickoff,
        decimal? spread,
        bool isSaturday,
        CancellationToken cancellationToken) =>
        RunAsync("Add a game", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            Game game = await InScopeAsync(async services =>
            {
                AppDbContext database = services.GetRequiredService<AppDbContext>();
                DateTime? first = await database.Games
                    .Where(candidate => candidate.SeasonYear == state.SeasonYear && candidate.Week == week && candidate.IsSaturdayEastern)
                    .MinAsync(candidate => (DateTime?)candidate.KickoffUtc, token)
                    .ConfigureAwait(false);

                DateTimeOffset kickoff = (first is DateTime value ? SimFormat.Utc(value) : _timeProvider.GetUtcNow().AddMinutes(state.LockLeadMinutes))
                    .AddMinutes(minutesAfterFirstKickoff);

                bool conference = await SameConferenceAsync(database, homeTeamId, awayTeamId, token).ConfigureAwait(false);
                return await services.GetRequiredService<SlateDesk>()
                    .AddGameAsync(state.SeasonYear, week, new SlateGameSpec(homeTeamId, awayTeamId, kickoff, spread, conference, isSaturday), token)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

            await EnsurePlansAsync(state, token).ConfigureAwait(false);
            log.Note($"added, kicking off {SimFormat.Eastern(SimFormat.Utc(game.KickoffUtc))}; it is in no set yet - add it in the app " +
                "(week games page) or run RegenerateGameSets to let the rules take it");
        }, cancellationToken);

    /// <summary>Moves one game's kickoff by <paramref name="by"/>; the week's locks follow.</summary>
    public Task<SimLog> MoveKickoffAsync(Guid gameId, TimeSpan by, CancellationToken cancellationToken) =>
        GameActionAsync("Move a kickoff", gameId, (state, game, log, token) =>
            SetKickoffCoreAsync(state, game, SimFormat.Utc(game.KickoffUtc) + by, log, token), cancellationToken);

    /// <summary>Sets one game's kickoff; the week's locks follow.</summary>
    public Task<SimLog> SetKickoffAsync(Guid gameId, DateTimeOffset kickoffUtc, CancellationToken cancellationToken) =>
        GameActionAsync("Set a kickoff", gameId, (state, game, log, token) =>
            SetKickoffCoreAsync(state, game, kickoffUtc, log, token), cancellationToken);

    /// <summary>Posts a new line for a game, or takes every line away with null.</summary>
    public Task<SimLog> SetSpreadAsync(Guid gameId, decimal? spread, CancellationToken cancellationToken) =>
        GameActionAsync("Set a line", gameId, async (state, game, log, token) =>
        {
            await InScopeAsync(services => services.GetRequiredService<SlateDesk>().SetSpreadAsync(gameId, spread, token)).ConfigureAwait(false);
            log.Note($"{Describe(game)}: {SimFormat.Spread(spread, Short(game.HomeTeam), Short(game.AwayTeam))}. Picks show it now, " +
                "and every open week's point value for it re-resolved (manual overrides stay)");
        }, cancellationToken);

    /// <summary>Flips a game's stored conference-game or Saturday flag.</summary>
    public Task<SimLog> SetGameFlagsAsync(Guid gameId, bool? isConferenceGame, bool? isSaturday, CancellationToken cancellationToken) =>
        GameActionAsync("Set game flags", gameId, async (state, game, log, token) =>
        {
            await InScopeAsync(services => services.GetRequiredService<SlateDesk>().SetFlagsAsync(gameId, isConferenceGame, isSaturday, token))
                .ConfigureAwait(false);
            log.Note($"{Describe(game)}: conference game {isConferenceGame ?? game.IsConferenceGame}, Saturday {isSaturday ?? game.IsSaturdayEastern}; " +
                "set generation sees the new flags on the next regeneration");
        }, cancellationToken);

    /// <summary>Deletes a game no set holds.</summary>
    public Task<SimLog> DeleteGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Delete a game", gameId, async (state, game, log, token) =>
        {
            await InScopeAsync(services => services.GetRequiredService<SlateDesk>().DeleteGameAsync(gameId, token)).ConfigureAwait(false);
            state.Plans.Remove(gameId);
            log.Note($"{Describe(game)} deleted");
        }, cancellationToken);

    /// <summary>Puts a team at <paramref name="rank"/> in this week's AP poll, or out of it with null.</summary>
    public Task<SimLog> SetRankAsync(Guid teamId, int? rank, CancellationToken cancellationToken) =>
        RunAsync("Set a rank", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            await InScopeAsync(services => services.GetRequiredService<SlateDesk>().SetRankAsync(state.SeasonYear, week, teamId, rank, token))
                .ConfigureAwait(false);
            log.Note(rank is int value
                ? $"ranked #{value}; a Top 25 rule sees it on the next regeneration"
                : "unranked; a Top 25 rule drops its game on the next regeneration");
        }, cancellationToken);

    /// <summary>
    /// Kicks a game off now: its kickoff moves to now - which moves a league's lock too if it was
    /// that set's earliest game, exactly as a real kickoff change would - and it goes live 0-0.
    /// </summary>
    public Task<SimLog> KickOffAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Kick off", gameId, async (state, game, log, token) =>
        {
            if (game.Status != GameStatus.Scheduled)
            {
                log.Note($"{Describe(game)} is already {game.Status}");
                return;
            }

            await KickOffCoreAsync(state, game, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Adds points for one side of a live game. Takes the game off autoplay.</summary>
    public Task<SimLog> ScoreAsync(Guid gameId, bool home, int points, CancellationToken cancellationToken) =>
        GameActionAsync($"+{points}", gameId, async (state, game, log, token) =>
        {
            if (game.Status != GameStatus.InProgress)
            {
                log.Note($"{Describe(game)} is {game.Status}; kick it off (or reopen it) first");
                return;
            }

            int homeScore = (game.HomeScore ?? 0) + (home ? points : 0);
            int awayScore = (game.AwayScore ?? 0) + (home ? 0 : points);
            await ApplyScoresAsync([new ScoreChange(game.Id, GameStatus.InProgress, homeScore, awayScore, game.Period, game.Clock)], token)
                .ConfigureAwait(false);
            TakeOffAutoplay(state, game);
            log.Note($"{Short(game.AwayTeam)} {awayScore}, {Short(game.HomeTeam)} {homeScore}");
        }, cancellationToken);

    /// <summary>Moves a live game to the next quarter, or to Final after the fourth.</summary>
    public Task<SimLog> NextQuarterAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Next quarter", gameId, async (state, game, log, token) =>
        {
            if (game.Status != GameStatus.InProgress)
            {
                log.Note($"{Describe(game)} is {game.Status}");
                return;
            }

            int quarter = (game.Period ?? 1) + 1;
            ScoreChange change = quarter > 4
                ? new ScoreChange(game.Id, GameStatus.Final, game.HomeScore ?? 0, game.AwayScore ?? 0, null, null)
                : new ScoreChange(game.Id, GameStatus.InProgress, game.HomeScore ?? 0, game.AwayScore ?? 0, (byte)quarter, QuarterStartClock);

            await ApplyScoresAsync([change], token).ConfigureAwait(false);
            TakeOffAutoplay(state, game);
            log.Note(quarter > 4 ? $"{Describe(game)}: Final" : $"{Describe(game)}: Q{quarter}");
        }, cancellationToken);

    /// <summary>
    /// Sets a game's score. <paramref name="final"/> ends it on that score (a tie is allowed, and
    /// lands on the app's "needs review" list); otherwise it is live. A scheduled game kicks off
    /// now first; a final game's result is rewritten and rescored. Takes the game off autoplay.
    /// </summary>
    public Task<SimLog> SetScoreAsync(Guid gameId, int home, int away, bool final, CancellationToken cancellationToken) =>
        GameActionAsync(final ? "Final score" : "Set the score", gameId, async (state, game, log, token) =>
        {
            home = Math.Max(0, home);
            away = Math.Max(0, away);

            if (game.Status is GameStatus.Postponed or GameStatus.Cancelled)
            {
                log.Note($"{Describe(game)} is {game.Status}; restore it first");
                return;
            }

            if (game.Status == GameStatus.Scheduled)
            {
                await KickOffCoreAsync(state, game, log, token).ConfigureAwait(false);
            }

            ScoreChange change = final
                ? new ScoreChange(game.Id, GameStatus.Final, home, away, null, null)
                : new ScoreChange(game.Id, GameStatus.InProgress, home, away, game.Period ?? 1, game.Clock ?? QuarterStartClock);

            if (game.Status == GameStatus.Final)
            {
                // The feed never walks a final game back, nor re-finals it on a new score.
                await InScopeAsync(services => services.GetRequiredService<ScoreDesk>().ForceAsync(change, token)).ConfigureAwait(false);
                log.Note("rewritten directly and rescored (a real feed never changes a final game)");
            }
            else
            {
                await ApplyScoresAsync([change], token).ConfigureAwait(false);
            }

            TakeOffAutoplay(state, game);
            log.Note($"{Short(game.AwayTeam)} {away}, {Short(game.HomeTeam)} {home}{(final ? " - Final" : string.Empty)}" +
                (final && home == away ? ": a tie, so no winner; the app lists it under 'needs review'" : string.Empty));
        }, cancellationToken);

    /// <summary>Ends a live game on its current score, or on its target if that is higher.</summary>
    public Task<SimLog> FinalAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Final", gameId, async (state, game, log, token) =>
        {
            if (game.Status != GameStatus.InProgress)
            {
                log.Note($"{Describe(game)} is {game.Status}; only a live game can go final (kick it off first)");
                return;
            }

            GamePlan plan = PlanFor(state, game, spread: null);
            int home = plan.Auto ? Math.Max(plan.TargetHome, game.HomeScore ?? 0) : game.HomeScore ?? 0;
            int away = plan.Auto ? Math.Max(plan.TargetAway, game.AwayScore ?? 0) : game.AwayScore ?? 0;

            await ApplyScoresAsync([new ScoreChange(game.Id, GameStatus.Final, home, away, null, null)], token).ConfigureAwait(false);
            TakeOffAutoplay(state, game);
            log.Note($"{Short(game.AwayTeam)} {away}, {Short(game.HomeTeam)} {home} - Final");
        }, cancellationToken);

    /// <summary>
    /// Postpones or cancels a game, or restores one to Scheduled. The app's schedule-change
    /// handler takes a postponed game out of every open set (and recomputes its lock), or flags a
    /// locked week's game for the commissioner to void.
    /// </summary>
    public Task<SimLog> SetDisruptionAsync(Guid gameId, GameStatus status, CancellationToken cancellationToken) =>
        GameActionAsync(status == GameStatus.Scheduled ? "Restore" : status.ToString(), gameId, async (state, game, log, token) =>
        {
            if (status is not (GameStatus.Postponed or GameStatus.Cancelled or GameStatus.Scheduled))
            {
                log.Fail($"{status} is not a disruption");
                return;
            }

            if (status == GameStatus.Scheduled && game.Status is not (GameStatus.Postponed or GameStatus.Cancelled))
            {
                log.Note($"{Describe(game)} is {game.Status}, not postponed or cancelled");
                return;
            }

            await ApplyScoresAsync([new ScoreChange(game.Id, status, null, null, null, null)], token).ConfigureAwait(false);

            if (status == GameStatus.Scheduled)
            {
                state.Plans[game.Id] = PlanFor(state, game, spread: null) with { Auto = true, AnchorProgress = 0, AnchorHome = 0, AnchorAway = 0 };
                log.Note($"{Describe(game)} is back on; an open set gets it back, and its lock is recomputed");
            }
            else
            {
                TakeOffAutoplay(state, game);
                log.Note($"{Describe(game)} {status}: removed from open sets; a locked week lists it for the commissioner to void");
            }
        }, cancellationToken);

    /// <summary>Puts a final game back to live (fourth quarter, 2:00) and rescores. Takes it off autoplay.</summary>
    public Task<SimLog> ReopenAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Reopen", gameId, async (state, game, log, token) =>
        {
            if (game.Status != GameStatus.Final)
            {
                log.Note($"{Describe(game)} is {game.Status}, not Final");
                return;
            }

            await InScopeAsync(services => services.GetRequiredService<ScoreDesk>()
                .ForceAsync(new ScoreChange(game.Id, GameStatus.InProgress, game.HomeScore, game.AwayScore, 4, "2:00"), token)).ConfigureAwait(false);
            TakeOffAutoplay(state, game);
            log.Note($"{Describe(game)} is live again (Q4 2:00) and the week is rescored without it");
        }, cancellationToken);

    /// <summary>Puts a game back to Scheduled with no score and back on autoplay from kickoff.</summary>
    public Task<SimLog> ResetGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        GameActionAsync("Reset a game", gameId, async (state, game, log, token) =>
        {
            await InScopeAsync(services => services.GetRequiredService<ScoreDesk>()
                .ForceAsync(new ScoreChange(game.Id, GameStatus.Scheduled, null, null, null, null), token)).ConfigureAwait(false);
            state.Plans[game.Id] = PlanFor(state, game, spread: null) with { Auto = true, AnchorProgress = 0, AnchorHome = 0, AnchorAway = 0 };

            DateTimeOffset kickoff = SimFormat.Utc(game.KickoffUtc);
            log.Note(kickoff <= _timeProvider.GetUtcNow()
                ? $"{Describe(game)} is Scheduled again - its kickoff has passed, so autoplay restarts it; move the kickoff to hold it"
                : $"{Describe(game)} is Scheduled again");
        }, cancellationToken);

    /// <summary>
    /// Sets where autoplay takes a game: the rest of the game is re-planned from the score it has
    /// now, and the game goes back on autoplay. A tie is allowed.
    /// </summary>
    public Task<SimLog> SetTargetAsync(Guid gameId, int home, int away, CancellationToken cancellationToken) =>
        GameActionAsync("Set the final score", gameId, (state, game, log, token) =>
        {
            GamePlan plan = PlanFor(state, game, spread: null) with { TargetHome = Math.Max(0, home), TargetAway = Math.Max(0, away), Auto = true };
            plan = game.Status == GameStatus.InProgress
                ? plan.AnchoredAt(Progress(game, _timeProvider.GetUtcNow(), state.GameMinutes), game.HomeScore ?? 0, game.AwayScore ?? 0)
                : plan with { AnchorProgress = 0, AnchorHome = 0, AnchorAway = 0 };
            state.Plans[game.Id] = plan;

            log.Note($"{Describe(game)} heads for {Short(game.AwayTeam)} {plan.TargetAway}, {Short(game.HomeTeam)} {plan.TargetHome}" +
                (game.Status == GameStatus.Final ? " (it is already final: reopen it to replay the ending)" : string.Empty));
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Puts one game on or off autoplay.</summary>
    public Task<SimLog> SetGameAutoAsync(Guid gameId, bool auto, CancellationToken cancellationToken) =>
        GameActionAsync(auto ? "Autoplay on" : "Autoplay off", gameId, (state, game, log, token) =>
        {
            GamePlan plan = PlanFor(state, game, spread: null) with { Auto = auto };
            if (auto && game.Status == GameStatus.InProgress)
            {
                plan = plan.AnchoredAt(Progress(game, _timeProvider.GetUtcNow(), state.GameMinutes), game.HomeScore ?? 0, game.AwayScore ?? 0);
            }

            state.Plans[game.Id] = plan;
            log.Note($"{Describe(game)}: autoplay {(auto ? "on" : "off")}");
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Ends every game of the week that has kicked off, each on its target (or its score, if higher).</summary>
    public Task<SimLog> FinishStartedAsync(CancellationToken cancellationToken) =>
        RunAsync("Finish every started game", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            List<Game> started = await InScopeAsync(async services =>
                (await services.GetRequiredService<SlateDesk>().GamesAsync(state.SeasonYear, week, token).ConfigureAwait(false))
                    .Where(game => game.Status == GameStatus.InProgress || (game.Status == GameStatus.Scheduled && game.KickoffUtc <= nowUtc))
                    .ToList()).ConfigureAwait(false);

            List<ScoreChange> finals =
            [
                .. started.Select(game =>
                {
                    GamePlan plan = PlanFor(state, game, spread: null);
                    return new ScoreChange(
                        game.Id,
                        GameStatus.Final,
                        Math.Max(plan.TargetHome, game.HomeScore ?? 0),
                        Math.Max(plan.TargetAway, game.AwayScore ?? 0),
                        null,
                        null);
                }),
            ];

            (int changed, int events) = await ApplyScoresAsync(finals, token).ConfigureAwait(false);
            await SaveStateAsync(state, token).ConfigureAwait(false);
            log.Note(started.Count == 0
                ? "nothing has kicked off yet (move the lock, or kick a game off)"
                : $"{changed} game(s) went final, {events} event(s) raised; the week is rescored");
        }, cancellationToken);

    /// <summary>
    /// One autoplay tick: every scheduled or live game of the season whose kickoff has passed moves
    /// to what its plan says for now - kicking off, scoring, the clock, the final whistle. Skipped
    /// (not queued) while another action holds the gate.
    /// </summary>
    /// <returns>True when a game moved.</returns>
    public async Task<bool> AutoplayTickAsync(CancellationToken cancellationToken)
    {
        if (!_status.IsUsable || !await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            SimState state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            if (!state.AutoPlay)
            {
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTime nowUtc = now.UtcDateTime;
            List<Game> due = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Games
                .AsNoTracking()
                .Include(game => game.HomeTeam)
                .Include(game => game.AwayTeam)
                .Where(game => game.SeasonYear == state.SeasonYear
                    && (game.Status == GameStatus.Scheduled || game.Status == GameStatus.InProgress)
                    && game.KickoffUtc <= nowUtc)
                .ToListAsync(cancellationToken)).ConfigureAwait(false);

            int plans = state.Plans.Count;
            var changes = new List<ScoreChange>();
            var log = new SimLog("Autoplay", now);

            foreach (Game game in due)
            {
                GamePlan plan = PlanFor(state, game, spread: null);
                if (!plan.Auto)
                {
                    continue;
                }

                PlannedState target = ScorePlan.At(ScorePlan.SeedFor(game.Id), plan, Progress(game, now, state.GameMinutes));
                int? home = target.Home is int planHome ? Math.Max(planHome, game.HomeScore ?? 0) : null;
                int? away = target.Away is int planAway ? Math.Max(planAway, game.AwayScore ?? 0) : null;

                if (target.Status == game.Status && home == game.HomeScore && away == game.AwayScore
                    && target.Period == game.Period && target.Clock == game.Clock)
                {
                    continue;
                }

                changes.Add(new ScoreChange(game.Id, target.Status, home, away, target.Period, target.Clock));

                if (game.Status == GameStatus.Scheduled)
                {
                    log.Note($"kickoff: {Describe(game)}");
                }

                if (target.Status == GameStatus.Final)
                {
                    log.Note(string.Create(CultureInfo.InvariantCulture, $"final: {Short(game.AwayTeam)} {away}, {Short(game.HomeTeam)} {home}"));
                }
            }

            if (state.Plans.Count != plans)
            {
                await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            }

            if (changes.Count == 0)
            {
                return false;
            }

            await ApplyScoresAsync(changes, cancellationToken).ConfigureAwait(false);

            if (log.Lines.Count > 0)
            {
                _activity.Add(log);
            }
            else
            {
                _activity.Touch();
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Autoplay tick failed; trying again next tick");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Locks whatever is due first - as the app's minute tick would have long before a real game
    /// ended - then feeds the scoreboard, so a final never lands on a week that is not locked yet.
    /// </summary>
    private async Task<(int Changed, int Events)> ApplyScoresAsync(IReadOnlyCollection<ScoreChange> changes, CancellationToken cancellationToken)
    {
        await InScopeAsync(services => services.GetRequiredService<WeekDesk>().LockDueAsync(cancellationToken)).ConfigureAwait(false);
        return await InScopeAsync(services => services.GetRequiredService<ScoreDesk>().ApplyAsync(changes, cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task KickOffCoreAsync(SimState state, Game game, SimLog log, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        await SetKickoffCoreAsync(state, game, nowUtc, log, cancellationToken).ConfigureAwait(false);

        await ApplyScoresAsync([new ScoreChange(game.Id, GameStatus.InProgress, 0, 0, 1, QuarterStartClock)], cancellationToken)
            .ConfigureAwait(false);
        state.Plans[game.Id] = PlanFor(state, game, spread: null) with { AnchorProgress = 0, AnchorHome = 0, AnchorAway = 0 };

        game.Status = GameStatus.InProgress;
        game.HomeScore = 0;
        game.AwayScore = 0;
        game.Period = 1;
        game.Clock = QuarterStartClock;
        game.KickoffUtc = nowUtc.UtcDateTime;
        log.Note($"{Describe(game)} kicked off");
    }

    private async Task SetKickoffCoreAsync(SimState state, Game game, DateTimeOffset kickoffUtc, SimLog log, CancellationToken cancellationToken)
    {
        await InScopeAsync(async services =>
        {
            SlateDesk slate = services.GetRequiredService<SlateDesk>();
            await slate.SetKickoffAsync(game.Id, kickoffUtc, cancellationToken).ConfigureAwait(false);
            await slate.RecomputeLocksAsync(state.SeasonYear, game.Week, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        log.Note($"{Describe(game)} now kicks off {SimFormat.Eastern(kickoffUtc)} ({SimFormat.Relative(kickoffUtc, nowUtc)}); open locks recomputed");

        int locked = await InScopeAsync(services => services.GetRequiredService<WeekDesk>().LockDueAsync(cancellationToken)).ConfigureAwait(false);
        if (locked > 0)
        {
            log.Note($"lock: that was a league's first game, so LockWeekJob locked {locked} week(s)");
        }
    }

    private Task<SimLog> GameActionAsync(
        string title,
        Guid gameId,
        Func<SimState, Game, SimLog, CancellationToken, Task> body,
        CancellationToken cancellationToken) =>
        RunAsync(title, async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            Game? game = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Games
                .AsNoTracking()
                .Include(candidate => candidate.HomeTeam)
                .Include(candidate => candidate.AwayTeam)
                .FirstOrDefaultAsync(candidate => candidate.Id == gameId, token)).ConfigureAwait(false);

            if (game is null)
            {
                log.Fail("that game no longer exists");
                return;
            }

            await body(state, game, log, token).ConfigureAwait(false);
            await SaveStateAsync(state, token).ConfigureAwait(false);
        }, cancellationToken);

    private static void TakeOffAutoplay(SimState state, Game game) =>
        state.Plans[game.Id] = PlanFor(state, game, spread: null) with { Auto = false };

    private static async Task<bool> SameConferenceAsync(AppDbContext database, Guid homeTeamId, Guid awayTeamId, CancellationToken cancellationToken)
    {
        List<string?> conferences = await database.Teams
            .AsNoTracking()
            .Where(team => team.Id == homeTeamId || team.Id == awayTeamId)
            .Select(team => team.Conference == null ? null : team.Conference.Abbreviation)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return conferences.Count == 2
            && conferences[0] is string conference
            && conference != SlateTeam.Independents
            && conference == conferences[1];
    }

    private static string Short(Team? team) =>
        team is null ? "?" : string.IsNullOrWhiteSpace(team.Abbreviation) ? team.School : team.Abbreviation;

    private static string Describe(Game game) => $"{Short(game.AwayTeam)} at {Short(game.HomeTeam)}";
}
