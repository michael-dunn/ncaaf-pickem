using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Catalog;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays CFBD's schedule, rankings and lines feeds for the simulated season: writes games, moves
/// kickoffs, posts lines and sets the AP poll - the rows an ingest writes - and then does what the
/// app does when a kickoff moves, recomputing each open week's <c>LockAtUtc</c> as the earliest
/// kickoff among its set's games (04-Domain-Algorithms section 2.9, the same rule
/// <c>ScheduleChangeHandler</c> applies).
/// </summary>
public sealed class SlateDesk
{
    /// <summary>Simulated games get CFBD ids from here up, far from any real one.</summary>
    public const long FirstSimulatedGameId = 9_000_000_000;

    /// <summary><c>GameLines.Provider</c> for every line the simulator posts.</summary>
    public const string LineProvider = "Simulator";

    /// <summary>How many teams the AP poll ranks.</summary>
    public const int PollSize = 25;

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the desk.</summary>
    public SlateDesk(AppDbContext database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <summary>The week's games, tracked, with both teams loaded, in kickoff order.</summary>
    public async Task<List<Game>> GamesAsync(int seasonYear, int week, CancellationToken cancellationToken) =>
        await _database.Games
            .Include(game => game.HomeTeam)
            .Include(game => game.AwayTeam)
            .Where(game => game.SeasonYear == seasonYear && game.Week == week)
            .OrderBy(game => game.KickoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>This week's AP ranks, by team id (the newest poll for the week).</summary>
    public async Task<Dictionary<Guid, int>> RanksAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        List<Ranking> rows = await _database.Rankings
            .AsNoTracking()
            .Where(row => row.SeasonYear == seasonYear && row.Week == week && row.Poll == Ranking.ApPoll)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.TeamId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Rank));
    }

    /// <summary>
    /// Writes the week's poll if it has none: last week's order with one or two neighbours
    /// swapped, or the catalog's preseason Top 25 for the first ranked week.
    /// </summary>
    /// <returns>True when a poll was written.</returns>
    public async Task<bool> EnsureRankingsAsync(int seasonYear, int week, TeamCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        bool exists = await _database.Rankings
            .AnyAsync(row => row.SeasonYear == seasonYear && row.Week == week, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return false;
        }

        int? previousWeek = await _database.Rankings
            .Where(row => row.SeasonYear == seasonYear && row.Week < week)
            .MaxAsync(row => (int?)row.Week, cancellationToken)
            .ConfigureAwait(false);

        List<Guid> order;
        if (previousWeek is int previous)
        {
            Dictionary<Guid, int> ranks = await RanksAsync(seasonYear, previous, cancellationToken).ConfigureAwait(false);
            order = [.. ranks.OrderBy(pair => pair.Value).Select(pair => pair.Key)];

            var random = new Random((seasonYear * 100) + week);
            for (int swap = 0; swap < 2 && order.Count > 1; swap++)
            {
                int index = random.Next(order.Count - 1);
                (order[index], order[index + 1]) = (order[index + 1], order[index]);
            }
        }
        else
        {
            Dictionary<int, Guid> byCfbdId = await _database.Teams
                .AsNoTracking()
                .ToDictionaryAsync(team => team.CfbdId, team => team.Id, cancellationToken)
                .ConfigureAwait(false);
            order = [.. catalog.PreseasonTop25.Where(byCfbdId.ContainsKey).Select(id => byCfbdId[id])];
        }

        await WritePollAsync(seasonYear, week, order, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Moves one team to <paramref name="rank"/> in the week's poll (everyone at or below it shifts
    /// down one; the poll stays at 25), or out of the poll when <paramref name="rank"/> is null.
    /// </summary>
    public async Task SetRankAsync(int seasonYear, int week, Guid teamId, int? rank, CancellationToken cancellationToken)
    {
        Dictionary<Guid, int> ranks = await RanksAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        List<Guid> order = [.. ranks.OrderBy(pair => pair.Value).Select(pair => pair.Key).Where(id => id != teamId)];

        if (rank is int position)
        {
            order.Insert(Math.Clamp(position, 1, Math.Min(PollSize, order.Count + 1)) - 1, teamId);
        }

        await WritePollAsync(seasonYear, week, [.. order.Take(PollSize)], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a quick slate with <see cref="SlateBuilder"/> from teams not already playing this
    /// week, and writes the games and their lines.
    /// </summary>
    /// <returns>The games written, with their teams loaded.</returns>
    public async Task<List<Game>> CreateSlateAsync(
        int seasonYear,
        int week,
        SlateOptions options,
        DateTimeOffset firstKickoffUtc,
        CancellationToken cancellationToken)
    {
        List<Game> existing = await GamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        HashSet<Guid> busy = [.. existing.SelectMany(game => new[] { game.HomeTeamId, game.AwayTeamId })];

        List<SlateTeam> teams = await _database.Teams
            .AsNoTracking()
            .Where(team => !busy.Contains(team.Id))
            .OrderBy(team => team.CfbdId)
            .Select(team => new SlateTeam(
                team.Id,
                team.School,
                team.Conference == null ? null : team.Conference.Abbreviation,
                team.Classification == TeamClassification.Fbs))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> ranks = await RanksAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SlateGameSpec> specs = SlateBuilder.Build(teams, ranks, options, firstKickoffUtc, (seasonYear * 100) + week + existing.Count);

        long nextId = await NextGameIdAsync(cancellationToken).ConfigureAwait(false);
        var games = new List<Game>(specs.Count);
        foreach (SlateGameSpec spec in specs)
        {
            games.Add(AddGame(seasonYear, week, nextId++, spec));
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        List<Guid> ids = [.. games.Select(game => game.Id)];
        return [.. (await GamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false)).Where(game => ids.Contains(game.Id))];
    }

    /// <summary>Writes one hand-picked game and its line.</summary>
    public async Task<Game> AddGameAsync(int seasonYear, int week, SlateGameSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.HomeTeamId == spec.AwayTeamId)
        {
            throw new InvalidOperationException("A team cannot play itself.");
        }

        bool busy = await _database.Games
            .AnyAsync(
                game => game.SeasonYear == seasonYear && game.Week == week
                    && (game.HomeTeamId == spec.HomeTeamId || game.AwayTeamId == spec.HomeTeamId
                        || game.HomeTeamId == spec.AwayTeamId || game.AwayTeamId == spec.AwayTeamId),
                cancellationToken)
            .ConfigureAwait(false);
        if (busy)
        {
            throw new InvalidOperationException("One of those teams already plays this week.");
        }

        long id = await NextGameIdAsync(cancellationToken).ConfigureAwait(false);
        Game game = AddGame(seasonYear, week, id, spec);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return game;
    }

    /// <summary>
    /// Deletes a game nobody has used yet. A game already in a league's set is refused: remove it
    /// from the set in the app, or postpone or cancel it here, as the real world would.
    /// </summary>
    public async Task DeleteGameAsync(Guid gameId, CancellationToken cancellationToken)
    {
        bool inSet = await _database.WeekGameSetGames.AnyAsync(row => row.GameId == gameId, cancellationToken).ConfigureAwait(false);
        if (inSet)
        {
            throw new InvalidOperationException(
                "That game is in a league's set. Remove it in the app, or postpone/cancel it here instead.");
        }

        await _database.GameLines.Where(line => line.GameId == gameId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await _database.Games.Where(game => game.Id == gameId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves one game's kickoff. Recompute the week's locks afterwards.</summary>
    public async Task<Game> SetKickoffAsync(Guid gameId, DateTimeOffset kickoffUtc, CancellationToken cancellationToken)
    {
        Game game = await _database.Games.FirstAsync(candidate => candidate.Id == gameId, cancellationToken).ConfigureAwait(false);
        SetKickoff(game, kickoffUtc);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return game;
    }

    /// <summary>
    /// Shifts every not-yet-started game of the week by the same amount so the earliest open lock
    /// (or, before any set exists, the earliest eligible kickoff) lands on <paramref name="lockAtUtc"/>,
    /// keeping the slate's spacing; then recomputes every open lock.
    /// </summary>
    /// <returns>How far the slate moved, or null when nothing is left to move.</returns>
    public async Task<TimeSpan?> ShiftLockAsync(int seasonYear, int week, DateTimeOffset lockAtUtc, CancellationToken cancellationToken)
    {
        await RecomputeLocksAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);

        DateTime? reference = await _database.WeekGameSets
            .Where(set => set.Week == week && set.League!.SeasonYear == seasonYear && set.LockedUtc == null && set.LockAtUtc != null)
            .MinAsync(set => set.LockAtUtc, cancellationToken)
            .ConfigureAwait(false);

        List<Game> games = await GamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        List<Game> scheduled = [.. games.Where(game => game.Status == GameStatus.Scheduled)];

        reference ??= scheduled.Where(IsEligible).Select(game => (DateTime?)game.KickoffUtc).Min()
            ?? scheduled.Select(game => (DateTime?)game.KickoffUtc).Min();

        if (reference is not DateTime from)
        {
            return null;
        }

        TimeSpan delta = lockAtUtc.UtcDateTime - from;
        foreach (Game game in scheduled)
        {
            SetKickoff(game, new DateTimeOffset(DateTime.SpecifyKind(game.KickoffUtc, DateTimeKind.Utc)) + delta);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecomputeLocksAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
        return delta;
    }

    /// <summary>Puts every not-yet-started game of the week on one kickoff instant (a played-out week).</summary>
    public async Task CollapseKickoffsAsync(int seasonYear, int week, DateTimeOffset kickoffUtc, CancellationToken cancellationToken)
    {
        foreach (Game game in (await GamesAsync(seasonYear, week, cancellationToken).ConfigureAwait(false))
            .Where(game => game.Status == GameStatus.Scheduled))
        {
            SetKickoff(game, kickoffUtc);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecomputeLocksAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a new line for the game (lines are history, as with the ingest: the newest wins), or
    /// with null removes every line, so the game shows none.
    /// </summary>
    public async Task SetSpreadAsync(Guid gameId, decimal? spread, CancellationToken cancellationToken)
    {
        if (spread is not decimal value)
        {
            await _database.GameLines.Where(line => line.GameId == gameId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _database.GameLines.Add(new GameLine
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            Provider = LineProvider,
            Spread = value,
            FetchedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Flips a game's stored conference-game or Saturday flag.</summary>
    public async Task SetFlagsAsync(Guid gameId, bool? isConferenceGame, bool? isSaturday, CancellationToken cancellationToken)
    {
        Game game = await _database.Games.FirstAsync(candidate => candidate.Id == gameId, cancellationToken).ConfigureAwait(false);
        game.IsConferenceGame = isConferenceGame ?? game.IsConferenceGame;
        game.IsSaturdayEastern = isSaturday ?? game.IsSaturdayEastern;
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// For every open (unlocked) set of the week, <c>LockAtUtc</c> = the earliest kickoff among its
    /// games that are not removed, or null with none left. Locked sets keep theirs.
    /// </summary>
    /// <returns>How many sets changed.</returns>
    public async Task<int> RecomputeLocksAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        var sets = await _database.WeekGameSets
            .Where(set => set.Week == week && set.League!.SeasonYear == seasonYear && set.LockedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sets.Count == 0)
        {
            return 0;
        }

        Guid[] setIds = [.. sets.Select(set => set.Id)];
        var earliest = await _database.WeekGameSetGames
            .Where(row => setIds.Contains(row.WeekGameSetId) && !row.IsRemoved)
            .GroupBy(row => row.WeekGameSetId)
            .Select(group => new { SetId = group.Key, First = group.Min(row => row.Game!.KickoffUtc) })
            .ToDictionaryAsync(row => row.SetId, row => row.First, cancellationToken)
            .ConfigureAwait(false);

        int changed = 0;
        foreach (var set in sets)
        {
            DateTime? lockAt = earliest.TryGetValue(set.Id, out DateTime first) ? first : null;
            if (set.LockAtUtc != lockAt)
            {
                set.LockAtUtc = lockAt;
                changed++;
            }
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <summary>What set generation's eligible pool takes: a Saturday game between two FBS teams.</summary>
    public static bool IsEligible(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.IsSaturdayEastern
            && game.HomeTeam?.Classification == TeamClassification.Fbs
            && game.AwayTeam?.Classification == TeamClassification.Fbs;
    }

    private static void SetKickoff(Game game, DateTimeOffset kickoffUtc)
    {
        game.KickoffUtc = kickoffUtc.UtcDateTime;
        game.KickoffEasternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(kickoffUtc).DateTime);
    }

    private Game AddGame(int seasonYear, int week, long cfbdGameId, SlateGameSpec spec)
    {
        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            CfbdGameId = cfbdGameId,
            SeasonYear = seasonYear,
            Week = week,
            HomeTeamId = spec.HomeTeamId,
            AwayTeamId = spec.AwayTeamId,
            IsSaturdayEastern = spec.IsSaturday,
            IsConferenceGame = spec.IsConferenceGame,
            Status = GameStatus.Scheduled,
        };
        SetKickoff(game, spec.KickoffUtc);
        _database.Games.Add(game);

        if (spec.Spread is decimal spread)
        {
            _database.GameLines.Add(new GameLine
            {
                Id = Guid.CreateVersion7(),
                GameId = game.Id,
                Provider = LineProvider,
                Spread = spread,
                FetchedUtc = _timeProvider.GetUtcNow().UtcDateTime,
            });
        }

        return game;
    }

    private async Task<long> NextGameIdAsync(CancellationToken cancellationToken)
    {
        long? highest = await _database.Games
            .Where(game => game.CfbdGameId >= FirstSimulatedGameId)
            .MaxAsync(game => (long?)game.CfbdGameId, cancellationToken)
            .ConfigureAwait(false);

        return (highest ?? (FirstSimulatedGameId - 1)) + 1;
    }

    private async Task WritePollAsync(int seasonYear, int week, IReadOnlyList<Guid> order, CancellationToken cancellationToken)
    {
        await _database.Rankings
            .Where(row => row.SeasonYear == seasonYear && row.Week == week && row.Poll == Ranking.ApPoll)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTime fetchedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        for (int index = 0; index < order.Count && index < PollSize; index++)
        {
            _database.Rankings.Add(new Ranking
            {
                SeasonYear = seasonYear,
                Week = week,
                Poll = Ranking.ApPoll,
                Rank = index + 1,
                TeamId = order[index],
                FetchedUtc = fetchedUtc,
            });
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
