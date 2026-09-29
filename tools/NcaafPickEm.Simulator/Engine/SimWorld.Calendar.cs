using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Catalog;

namespace NcaafPickEm.Simulator.Engine;

/// <content>The calendar and a week's lifecycle: start, lock, rewind, reset, play out.</content>
public sealed partial class SimWorld
{
    /// <summary>
    /// Makes <paramref name="week"/> the current week from now: the calendar is laid out around it,
    /// and if it has no games yet it gets a poll and a slate whose first kickoff is
    /// <see cref="SimState.LockLeadMinutes"/> away; then the app's Sunday job creates every
    /// league's set.
    /// </summary>
    public Task<SimLog> StartWeekAsync(int week, CancellationToken cancellationToken) =>
        RunAsync($"Start week {week}", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            await EnterWeekAsync(state, Math.Clamp(week, SimCalendarLayout.FirstWeek, SimCalendarLayout.LastRegularWeek), log, token)
                .ConfigureAwait(false);
            await NoteLocksAsync(state, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Ends the current week now and starts the next one (see <see cref="StartWeekAsync"/>).</summary>
    public Task<SimLog> NextWeekAsync(CancellationToken cancellationToken) =>
        RunAsync("Next week", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            if (week >= SimCalendarLayout.LastRegularWeek)
            {
                log.Note($"week {week} is the last regular week; use 'Season over' instead");
                return;
            }

            await EnterWeekAsync(state, week + 1, log, token).ConfigureAwait(false);
            await NoteLocksAsync(state, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Moves the whole season a day into the future: the app reads "before the season".</summary>
    public Task<SimLog> BeforeSeasonAsync(CancellationToken cancellationToken) =>
        RunAsync("Before the season", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.BeforeSeason(state.SeasonYear, _timeProvider.GetUtcNow());
            await InScopeAsync(services => services.GetRequiredService<CalendarDesk>().WriteAsync(state.SeasonYear, weeks, token))
                .ConfigureAwait(false);
            log.Note($"week 1 now opens {SimFormat.EasternDate(weeks[0].StartUtc)}; the app shows 'season starts'");
        }, cancellationToken);

    /// <summary>Closes the last regular week a minute ago: the app reads "season over" and leagues complete.</summary>
    public Task<SimLog> SeasonOverAsync(CancellationToken cancellationToken) =>
        RunAsync("Season over", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.SeasonOver(state.SeasonYear, _timeProvider.GetUtcNow());
            await InScopeAsync(services => services.GetRequiredService<CalendarDesk>().WriteAsync(state.SeasonYear, weeks, token))
                .ConfigureAwait(false);
            log.Note($"week {SimCalendarLayout.LastRegularWeek} closed a minute ago; every league in {state.SeasonYear} is complete");
        }, cancellationToken);

    /// <summary>
    /// Plays <paramref name="weeks"/> weeks out instantly, one after another: fill every missing
    /// pick and submit, kick everything off now, lock, finish every game on its plan, then start
    /// the next week - which is left ready to play by hand. Builds a season's history (standings,
    /// weekly wins, trend arrows) in seconds.
    /// </summary>
    /// <param name="weeks">How many weeks, 1..14.</param>
    /// <param name="includeTester">Fill the tester's missing picks too.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public Task<SimLog> PlayOutWeeksAsync(int weeks, bool includeTester, CancellationToken cancellationToken) =>
        RunAsync(weeks == 1 ? "Play out the week" : $"Play out {weeks} weeks", async (log, token) =>
        {
            for (int played = 0; played < Math.Clamp(weeks, 1, SimCalendarLayout.LastRegularWeek); played++)
            {
                SimState state = await LoadStateAsync(token).ConfigureAwait(false);
                CurrentWeek? current = await CurrentWeekAsync(state, token).ConfigureAwait(false);

                if (current is not CurrentWeek now || now.State == SeasonState.BeforeSeason)
                {
                    await EnterWeekAsync(state, SimCalendarLayout.FirstWeek, log, token).ConfigureAwait(false);
                    current = await CurrentWeekAsync(state, token).ConfigureAwait(false);
                }

                if (current is not CurrentWeek { State: SeasonState.InSeason } week)
                {
                    log.Note("the season is over; nothing left to play");
                    return;
                }

                await PlayOutAsync(state, week.Week, includeTester, log, token).ConfigureAwait(false);

                if (week.Week >= SimCalendarLayout.LastRegularWeek)
                {
                    IReadOnlyList<SeasonWeek> over = SimCalendarLayout.SeasonOver(state.SeasonYear, _timeProvider.GetUtcNow());
                    await InScopeAsync(services => services.GetRequiredService<CalendarDesk>().WriteAsync(state.SeasonYear, over, token))
                        .ConfigureAwait(false);
                    log.Note("that was the last regular week: the season is over");
                    return;
                }

                await EnterWeekAsync(state, week.Week + 1, log, token).ConfigureAwait(false);
            }

            await NoteLocksAsync(await LoadStateAsync(token).ConfigureAwait(false), log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Moves the week's first kickoff - and with it every league's lock - to
    /// <paramref name="lockAtUtc"/>, shifting the rest of the slate by the same amount. A lock in
    /// the past (or now) locks immediately, through the app's lock job.
    /// </summary>
    public Task<SimLog> SetLockAsync(DateTimeOffset lockAtUtc, CancellationToken cancellationToken) =>
        RunAsync("Move the lock", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            await ShiftLockAsync(state, week, lockAtUtc, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>First kickoff now, and lock straight away rather than on the app's next minute tick.</summary>
    public Task<SimLog> LockNowAsync(CancellationToken cancellationToken) =>
        RunAsync("Lock now", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            await ShiftLockAsync(state, week, _timeProvider.GetUtcNow(), log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Takes the week back to before its lock with every pick kept (see
    /// <see cref="WeekDesk.RewindAsync"/>), resets every game's autoplay, and puts the first
    /// kickoff <see cref="SimState.LockLeadMinutes"/> from now - so the lock, the dashboard and
    /// scoring can be watched again.
    /// </summary>
    public Task<SimLog> RewindWeekAsync(CancellationToken cancellationToken) =>
        RunAsync("Rewind to before the lock", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            int sets = await InScopeAsync(services => services.GetRequiredService<WeekDesk>().RewindAsync(state.SeasonYear, week, token))
                .ConfigureAwait(false);
            log.Note($"{sets} set(s) unlocked; picks kept, statuses recomputed; results, corrections and this week's notifications cleared");

            await ResetPlansAsync(state, week, token).ConfigureAwait(false);
            await ShiftLockAsync(state, week, _timeProvider.GetUtcNow().AddMinutes(state.LockLeadMinutes), log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Wipes the week back to a blank one (see <see cref="WeekDesk.ResetAsync"/>), with the same
    /// slate kicking off <see cref="SimState.LockLeadMinutes"/> from now and every league's set
    /// generated afresh.
    /// </summary>
    public Task<SimLog> ResetWeekAsync(CancellationToken cancellationToken) =>
        RunAsync("Reset the week", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            int sets = await InScopeAsync(services => services.GetRequiredService<WeekDesk>().ResetAsync(state.SeasonYear, week, token))
                .ConfigureAwait(false);
            log.Note($"{sets} set(s) deleted with their picks, statuses and results; games back to Scheduled");

            await ResetPlansAsync(state, week, token).ConfigureAwait(false);
            await ShiftLockAsync(state, week, _timeProvider.GetUtcNow().AddMinutes(state.LockLeadMinutes), log, token).ConfigureAwait(false);
            await RunJobStepAsync("EnsureCurrentWeekSets", log, token).ConfigureAwait(false);
            await NoteLocksAsync(state, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Resets the week and replaces its games with a brand-new slate.</summary>
    public Task<SimLog> ReplaceSlateAsync(CancellationToken cancellationToken) =>
        RunAsync("New slate", async (log, token) =>
        {
            SimState state = await LoadStateAsync(token).ConfigureAwait(false);
            if (await RequireWeekAsync(state, log, token).ConfigureAwait(false) is not int week)
            {
                return;
            }

            await InScopeAsync(async services =>
            {
                WeekDesk desk = services.GetRequiredService<WeekDesk>();
                await desk.ResetAsync(state.SeasonYear, week, token).ConfigureAwait(false);
                int deleted = await desk.DeleteGamesAsync(state.SeasonYear, week, token).ConfigureAwait(false);
                log.Note($"week {week}: sets and {deleted} game(s) deleted");
            }).ConfigureAwait(false);

            await CreateSlateAsync(state, week, log, token).ConfigureAwait(false);
            await RunJobStepAsync("EnsureCurrentWeekSets", log, token).ConfigureAwait(false);
            await NoteLocksAsync(state, log, token).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Lays the calendar out with <paramref name="week"/> current from now, and makes the week ready to pick.</summary>
    private async Task EnterWeekAsync(SimState state, int week, SimLog log, CancellationToken cancellationToken, bool generateSets = true)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.Build(state.SeasonYear, week, nowUtc);
        await InScopeAsync(services => services.GetRequiredService<CalendarDesk>().WriteAsync(state.SeasonYear, weeks, cancellationToken))
            .ConfigureAwait(false);
        log.Note($"calendar: week {week} is current, until {SimFormat.EasternDate(weeks[week - SimCalendarLayout.FirstWeek].EndUtc)}");

        bool hasGames = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Games
            .AnyAsync(game => game.SeasonYear == state.SeasonYear && game.Week == week, cancellationToken)).ConfigureAwait(false);

        if (!hasGames)
        {
            await CreateSlateAsync(state, week, log, cancellationToken).ConfigureAwait(false);
        }

        if (generateSets)
        {
            await RunJobStepAsync("EnsureCurrentWeekSets", log, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The week's poll (if it has none) and a quick slate, first kickoff a lock-lead from now.</summary>
    private async Task CreateSlateAsync(SimState state, int week, SimLog log, CancellationToken cancellationToken)
    {
        DateTimeOffset firstKickoff = _timeProvider.GetUtcNow().AddMinutes(state.LockLeadMinutes);

        int created = await InScopeAsync(async services =>
        {
            SlateDesk slate = services.GetRequiredService<SlateDesk>();
            await slate.EnsureRankingsAsync(state.SeasonYear, week, TeamCatalog.Default, cancellationToken).ConfigureAwait(false);
            return (await slate.CreateSlateAsync(state.SeasonYear, week, state.Slate, firstKickoff, cancellationToken).ConfigureAwait(false)).Count;
        }).ConfigureAwait(false);

        await EnsurePlansAsync(state, cancellationToken).ConfigureAwait(false);
        log.Note($"slate: {created} game(s), first kickoff {SimFormat.Eastern(firstKickoff)}");
    }

    /// <summary>Everything "play out" does to one week.</summary>
    private async Task PlayOutAsync(SimState state, int week, bool includeTester, SimLog log, CancellationToken cancellationToken)
    {
        // Quietly: entering the week already ran it; this catches a league made in the app since.
        await RunJobStepAsync("EnsureCurrentWeekSets", log, cancellationToken, record: false).ConfigureAwait(false);

        int filled = await FillEveryoneAsync(state, week, PickStrategy.Mixed, includeTester, leaveUnpicked: 0, submit: true, log, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        await InScopeAsync(services => services.GetRequiredService<SlateDesk>().CollapseKickoffsAsync(state.SeasonYear, week, nowUtc, cancellationToken))
            .ConfigureAwait(false);

        List<ScoreChange> finals = await InScopeAsync(async services =>
        {
            List<Game> games = await services.GetRequiredService<SlateDesk>().GamesAsync(state.SeasonYear, week, cancellationToken).ConfigureAwait(false);
            return games
                .Where(game => game.Status is GameStatus.Scheduled or GameStatus.InProgress)
                .Select(game =>
                {
                    GamePlan plan = PlanFor(state, game, spread: null);
                    return new ScoreChange(
                        game.Id,
                        GameStatus.Final,
                        Math.Max(plan.TargetHome, game.HomeScore ?? 0),
                        Math.Max(plan.TargetAway, game.AwayScore ?? 0),
                        null,
                        null);
                })
                .ToList();
        }).ConfigureAwait(false);

        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        (int changed, _) = await ApplyScoresAsync(finals, cancellationToken).ConfigureAwait(false);
        log.Note($"week {week}: {filled} player(s) filled in, locked, {changed} game(s) went final");

        await NoteWinnersAsync(state, week, log, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Notes each league's week winner (or leaders, while not complete).</summary>
    private async Task NoteWinnersAsync(SimState state, int week, SimLog log, CancellationToken cancellationToken)
    {
        var results = await InScopeAsync(services =>
        {
            AppDbContext database = services.GetRequiredService<AppDbContext>();
            return database.WeekResults
                .AsNoTracking()
                .Where(row => row.WeekGameSet!.Week == week && row.WeekGameSet.League!.SeasonYear == state.SeasonYear)
                .Select(row => new
                {
                    League = row.WeekGameSet!.League!.Name,
                    Name = row.Membership!.DisplayNameOverride ?? row.Membership.User!.DisplayName,
                    row.Points,
                    row.IsWeekComplete,
                })
                .ToListAsync(cancellationToken);
        }).ConfigureAwait(false);

        foreach (var league in results.GroupBy(row => row.League))
        {
            int best = league.Max(row => row.Points);
            string leaders = string.Join(", ", league.Where(row => row.Points == best).Select(row => row.Name));
            string verdict = league.All(row => row.IsWeekComplete) ? "won" : "leads (week not complete)";
            log.Note($"{league.Key}: {leaders} {verdict} with {best}");
        }
    }

    /// <summary>
    /// Shifts the slate so the earliest open lock is <paramref name="lockAtUtc"/>, then runs the
    /// lock job for anything now due.
    /// </summary>
    private async Task ShiftLockAsync(SimState state, int week, DateTimeOffset lockAtUtc, SimLog log, CancellationToken cancellationToken)
    {
        TimeSpan? moved = await InScopeAsync(services => services.GetRequiredService<SlateDesk>()
            .ShiftLockAsync(state.SeasonYear, week, lockAtUtc, cancellationToken)).ConfigureAwait(false);

        if (moved is not TimeSpan delta)
        {
            log.Note("nothing to move: no game this week is still scheduled");
            return;
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        log.Note($"slate moved {(delta >= TimeSpan.Zero ? "later" : "earlier")} by {SimFormat.Duration(delta)}; " +
            $"first kickoff {SimFormat.Eastern(lockAtUtc)} ({SimFormat.Relative(lockAtUtc, nowUtc)})");

        int locked = await InScopeAsync(services => services.GetRequiredService<WeekDesk>().LockDueAsync(cancellationToken)).ConfigureAwait(false);
        if (locked > 0)
        {
            log.Note($"lock: LockWeekJob locked {locked} week(s) - picks are frozen and visible to everyone");
        }

        await NoteLocksAsync(state, log, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives every game of the season that has no plan a default one.</summary>
    private async Task EnsurePlansAsync(SimState state, CancellationToken cancellationToken)
    {
        await InScopeAsync(async services =>
        {
            AppDbContext database = services.GetRequiredService<AppDbContext>();
            List<Guid> missing = await database.Games
                .AsNoTracking()
                .Where(game => game.SeasonYear == state.SeasonYear)
                .Select(game => game.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            missing.RemoveAll(state.Plans.ContainsKey);

            Dictionary<Guid, decimal> spreads = await PointValueRecalculator
                .LoadLatestSpreadsAsync(database, missing, cancellationToken)
                .ConfigureAwait(false);

            foreach (Guid gameId in missing)
            {
                (int home, int away) = ScorePlan.DefaultTarget(ScorePlan.SeedFor(gameId), spreads.TryGetValue(gameId, out decimal spread) ? spread : null);
                state.Plans[gameId] = new GamePlan(home, away);
            }
        }).ConfigureAwait(false);

        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Back to autoplay from kickoff, targets kept, for every game of the week.</summary>
    private async Task ResetPlansAsync(SimState state, int week, CancellationToken cancellationToken)
    {
        List<Guid> ids = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Games
            .AsNoTracking()
            .Where(game => game.SeasonYear == state.SeasonYear && game.Week == week)
            .Select(game => game.Id)
            .ToListAsync(cancellationToken)).ConfigureAwait(false);

        foreach (Guid id in ids)
        {
            if (state.Plans.TryGetValue(id, out GamePlan? plan))
            {
                state.Plans[id] = plan with { Auto = true, AnchorProgress = 0, AnchorHome = 0, AnchorAway = 0 };
            }
        }

        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        await EnsurePlansAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fills every member's missing picks in every league this week; returns how many members.</summary>
    private async Task<int> FillEveryoneAsync(
        SimState state,
        int week,
        PickStrategy strategy,
        bool includeTester,
        int leaveUnpicked,
        bool submit,
        SimLog log,
        CancellationToken cancellationToken)
    {
        var members = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Memberships
            .AsNoTracking()
            .Where(membership => membership.League!.SeasonYear == state.SeasonYear && membership.RemovedUtc == null)
            .OrderBy(membership => membership.LeagueId)
            .ThenByDescending(membership => membership.Role)
            .ThenBy(membership => membership.JoinedUtc)
            .Select(membership => new
            {
                membership.Id,
                membership.LeagueId,
                membership.UserId,
                Name = membership.DisplayNameOverride ?? membership.User!.DisplayName,
            })
            .ToListAsync(cancellationToken)).ConfigureAwait(false);

        int filled = 0;
        var refusals = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var league in members.GroupBy(member => member.LeagueId))
        {
            int variant = 0;
            foreach (var member in league)
            {
                int position = variant++;
                if (!includeTester && member.UserId == state.TesterUserId)
                {
                    continue;
                }

                try
                {
                    string outcome = await InScopeAsync(services => services.GetRequiredService<PeopleDesk>()
                        .FillAsync(member.Id, week, strategy, position, leaveUnpicked, submit, cancellationToken)).ConfigureAwait(false);
                    filled++;
                    _logger.LogDebug("Filled {Member}: {Outcome}", member.Name, outcome);
                }
                catch (Exception exception) when (exception is PickRuleViolation or InvalidOperationException)
                {
                    if (!refusals.TryGetValue(exception.Message, out List<string>? names))
                    {
                        refusals[exception.Message] = names = [];
                    }

                    names.Add(member.Name);
                }
            }
        }

        // One line per distinct refusal: a locked week refuses everybody the same way.
        foreach ((string reason, List<string> names) in refusals)
        {
            log.Note($"picks: {reason} ({string.Join(", ", names)})");
        }

        return filled;
    }

    /// <summary>The game's plan, creating the default one on first use.</summary>
    private static GamePlan PlanFor(SimState state, Game game, decimal? spread)
    {
        if (!state.Plans.TryGetValue(game.Id, out GamePlan? plan))
        {
            (int home, int away) = ScorePlan.DefaultTarget(ScorePlan.SeedFor(game.Id), spread);
            plan = new GamePlan(home, away);
            state.Plans[game.Id] = plan;
        }

        return plan;
    }

    /// <summary>Re-anchors every live game's plan at its current score (see <see cref="GamePlan.AnchoredAt"/>).</summary>
    private async Task ReanchorLiveGamesAsync(SimState state, DateTimeOffset nowUtc, int gameMinutes, CancellationToken cancellationToken)
    {
        List<Game> live = await InScopeAsync(services => services.GetRequiredService<AppDbContext>().Games
            .AsNoTracking()
            .Where(game => game.SeasonYear == state.SeasonYear && game.Status == GameStatus.InProgress)
            .ToListAsync(cancellationToken)).ConfigureAwait(false);

        foreach (Game game in live)
        {
            GamePlan plan = PlanFor(state, game, spread: null);
            state.Plans[game.Id] = plan.AnchoredAt(Progress(game, nowUtc, gameMinutes), game.HomeScore ?? 0, game.AwayScore ?? 0);
        }
    }

    private static double Progress(Game game, DateTimeOffset nowUtc, int gameMinutes) =>
        (nowUtc - SimFormat.Utc(game.KickoffUtc)).TotalMinutes / Math.Max(1, gameMinutes);
}
