using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for game-set configuration (Feature 02, P3-03): loads inputs for
/// <see cref="GameSetGenerator"/>, persists whatever it decides, recomputes point values through
/// <see cref="PointValueRecalculator"/>, and raises <see cref="GameAddedToSet"/> /
/// <see cref="GameRemovedFromSet"/>. Endpoint handlers stay thin.
/// </summary>
public sealed class GameSetService
{
    private const string ReasonGenerated = "Generated";
    private const string ReasonRuleRegeneration = "Rule regeneration";
    private const string ReasonScheduleChange = "Schedule change";
    private const string ReasonManual = "Manual";

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly DomainEventCollector _collector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ISeasonWeekSource _seasonWeekSource;

    /// <summary>Creates the service.</summary>
    public GameSetService(
        AppDbContext database,
        TimeProvider timeProvider,
        DomainEventCollector collector,
        IDomainEventDispatcher dispatcher,
        ISeasonWeekSource seasonWeekSource)
    {
        _database = database;
        _timeProvider = timeProvider;
        _collector = collector;
        _dispatcher = dispatcher;
        _seasonWeekSource = seasonWeekSource;
    }

    /// <summary>
    /// Returns the week's <c>WeekGameSets</c> row, creating it (empty, <c>UsesOverride=false</c>)
    /// if it does not exist yet. P3-04's Tuesday job calls this before generating.
    /// </summary>
    public async Task<WeekGameSet> GetOrCreateWeekSetAsync(Guid leagueId, int week, CancellationToken cancellationToken)
    {
        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false);

        if (set is not null)
        {
            return set;
        }

        set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Week = week,
            UsesOverride = false,
            GeneratedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _database.WeekGameSets.Add(set);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return set;
    }

    /// <summary>Previews what candidate rules would select, without persisting anything.</summary>
    public async Task<GameSetPreview> PreviewAsync(
        Guid leagueId,
        int week,
        IReadOnlyList<GameSetRuleDto> candidateRules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateRules);

        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ExistingSetGame> existing = set is null
            ? []
            : await LoadExistingSetGamesAsync(set.Id, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, int?> overridesByGameId = set is null
            ? []
            : await _database.WeekGameSetGames
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == set.Id)
                .ToDictionaryAsync(row => row.GameId, row => row.PointValueOverride, cancellationToken)
                .ConfigureAwait(false);

        List<GameInfo> games = await LoadGameInfosAsync(league.SeasonYear, week, cancellationToken).ConfigureAwait(false);
        List<RankingSet> rankings = await LoadRankingSetsAsync(league.SeasonYear, week, cancellationToken).ConfigureAwait(false);
        List<RuleInfo> rules = [.. candidateRules.Select(ToRuleInfo)];

        var request = new GameSetGenerationRequest
        {
            Week = week,
            Games = games,
            Rules = rules,
            Rankings = rankings,
            ExistingGames = existing,
            IsLocked = false,
        };

        GenerationResult result = GameSetGenerator.Preview(request);

        GameSetGameDto[] dtos = await BuildDtosAsync(
            leagueId,
            league,
            week,
            result.Games,
            games,
            overridesByGameId,
            cancellationToken).ConfigureAwait(false);

        return new GameSetPreview(dtos, result.Count, result.ExceedsMax, result.UsedFallbackRankings);
    }

    /// <summary>
    /// Regenerates the week's set from its saved rules (default, or the week override when
    /// <c>UsesOverride</c> is set), persists the diff, recomputes point values, and raises
    /// <see cref="GameAddedToSet"/>/<see cref="GameRemovedFromSet"/>.
    /// </summary>
    /// <exception cref="GameSetRuleViolation">
    /// <see cref="GameSetRuleViolationCode.Locked"/> when the week is already locked, or
    /// <see cref="GameSetRuleViolationCode.ExceedsMax"/> when the rules would select more than
    /// <see cref="GameSetLimits.MaxGames"/> games. Neither refusal persists anything.
    /// </exception>
    public async Task<WeekGameSetResponse> GenerateAsync(Guid leagueId, int week, CancellationToken cancellationToken)
    {
        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet set = await GetOrCreateWeekSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        List<WeekGameSetGame> existingRows = await _database.WeekGameSetGames
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.WeekGameSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RuleInfo> rules = await LoadSavedRulesAsync(leagueId, week, set.UsesOverride, cancellationToken)
            .ConfigureAwait(false);

        List<GameInfo> games = await LoadGameInfosAsync(league.SeasonYear, week, cancellationToken).ConfigureAwait(false);
        List<RankingSet> rankings = await LoadRankingSetsAsync(league.SeasonYear, week, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ExistingSetGame> existing =
            [.. existingRows.Select(row => new ExistingSetGame(row.GameId, row.Source, row.IsRemoved))];

        var request = new GameSetGenerationRequest
        {
            Week = week,
            Games = games,
            Rules = rules,
            Rankings = rankings,
            ExistingGames = existing,
            IsLocked = set.IsLocked,
        };

        GenerationResult result = GameSetGenerator.Generate(request);

        if (result.IsLocked)
        {
            throw new GameSetRuleViolation(GameSetRuleViolationCode.Locked, "This week is already locked.");
        }

        if (result.ExceedsMax)
        {
            throw new GameSetRuleViolation(
                GameSetRuleViolationCode.ExceedsMax,
                $"The configured rules would select {result.Count} games; the maximum is {GameSetLimits.MaxGames}.",
                result.Count);
        }

        Dictionary<Guid, WeekGameSetGame> existingByGameId = existingRows.ToDictionary(row => row.GameId);
        Dictionary<Guid, GameInfo> pool = games.ToDictionary(g => g.GameId);
        HashSet<Guid> removedIds = [.. result.Removed];
        HashSet<Guid> removedIneligibleIds = [.. result.RemovedIneligible];

        var addedGameSetGameIds = new List<Guid>();
        var removedEventTargets = new List<(Guid GameSetGameId, Guid GameId, string Reason)>();

        foreach (Guid gameId in removedIds)
        {
            WeekGameSetGame row = existingByGameId[gameId];
            row.IsRemoved = true;
            row.RemovedReason = ReasonRuleRegeneration;
            removedEventTargets.Add((row.Id, row.GameId, ReasonRuleRegeneration));
        }

        foreach (Guid gameId in removedIneligibleIds)
        {
            WeekGameSetGame row = existingByGameId[gameId];
            row.IsRemoved = true;
            row.RemovedReason = ReasonScheduleChange;
            removedEventTargets.Add((row.Id, row.GameId, ReasonScheduleChange));
        }

        foreach (GeneratedGame generated in result.Games)
        {
            if (existingByGameId.TryGetValue(generated.GameId, out WeekGameSetGame? row))
            {
                row.Source = generated.Source;
                continue;
            }

            var newRow = new WeekGameSetGame
            {
                Id = Guid.CreateVersion7(),
                WeekGameSetId = set.Id,
                GameId = generated.GameId,
                Source = generated.Source,
                IsRemoved = false,
            };

            // Load the Game+Teams navigation for point-value recalculation below without a
            // second query per row.
            GameInfo info = pool[generated.GameId];
            newRow.Game = await _database.Games
                .Include(g => g.HomeTeam)
                .Include(g => g.AwayTeam)
                .FirstAsync(g => g.Id == info.GameId, cancellationToken)
                .ConfigureAwait(false);

            _database.WeekGameSetGames.Add(newRow);
            existingRows.Add(newRow);
            addedGameSetGameIds.Add(newRow.Id);
        }

        set.LockAtUtc = result.LockAtUtc;
        set.GeneratedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await PointValueRecalculator.RecalculateAsync(_database, leagueId, league.DefaultPointValue, existingRows, cancellationToken)
            .ConfigureAwait(false);

        if (addedGameSetGameIds.Count > 0)
        {
            _collector.Raise(new GameAddedToSet(leagueId, week, set.Id, addedGameSetGameIds, ReasonGenerated)
            {
                OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
            });
        }

        foreach ((Guid gameSetGameId, Guid gameId, string reason) in removedEventTargets)
        {
            _collector.Raise(new GameRemovedFromSet(leagueId, week, set.Id, gameSetGameId, gameId, reason)
            {
                OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
            });
        }

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);

        return await GetWeekGameSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Manually adds a game. 409 when the week is locked, the game is not Saturday-Eastern FBS
    /// (or is postponed/cancelled), or the set already holds <see cref="GameSetLimits.MaxGames"/>
    /// active games. Re-adding a game that was previously removed flips it back to Manual rather
    /// than inserting a duplicate row.
    /// </summary>
    public async Task<WeekGameSetResponse> AddGameAsync(
        Membership caller,
        int week,
        Guid gameId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Guid leagueId = caller.LeagueId;
        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet set = await GetOrCreateWeekSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        if (set.IsLocked)
        {
            throw new GameSetRuleViolation(GameSetRuleViolationCode.Locked, "This week is already locked.");
        }

        Game game = await _database.Games
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .FirstOrDefaultAsync(g => g.Id == gameId && g.SeasonYear == league.SeasonYear && g.Week == week, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new GameSetRuleViolation(GameSetRuleViolationCode.GameNotFound, "No such game in this week.");

        bool eligible = game.IsSaturdayEastern
            && game.HomeTeam!.Classification == TeamClassification.Fbs
            && game.AwayTeam!.Classification == TeamClassification.Fbs
            && game.Status is not (GameStatus.Postponed or GameStatus.Cancelled);

        if (!eligible)
        {
            throw new GameSetRuleViolation(
                GameSetRuleViolationCode.GameNotEligible,
                "Only Saturday-Eastern games between two FBS teams that are still on the schedule may be added.");
        }

        List<WeekGameSetGame> existingRows = await _database.WeekGameSetGames
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.WeekGameSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        WeekGameSetGame? row = existingRows.FirstOrDefault(r => r.GameId == gameId);

        if (row is not null && !row.IsRemoved)
        {
            // Already active: idempotent no-op.
            return await GetWeekGameSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
        }

        int activeCount = existingRows.Count(r => !r.IsRemoved);
        if (row is null && activeCount >= GameSetLimits.MaxGames)
        {
            throw new GameSetRuleViolation(
                GameSetRuleViolationCode.ExceedsMax,
                $"This week already has {activeCount} games; the maximum is {GameSetLimits.MaxGames}.",
                activeCount + 1);
        }

        Guid gameSetGameId;
        if (row is not null)
        {
            row.IsRemoved = false;
            row.RemovedReason = null;
            row.Source = GameSetGameSource.Manual;
            row.Game = game;
            gameSetGameId = row.Id;
        }
        else
        {
            row = new WeekGameSetGame
            {
                Id = Guid.CreateVersion7(),
                WeekGameSetId = set.Id,
                GameId = gameId,
                Source = GameSetGameSource.Manual,
                IsRemoved = false,
                Game = game,
            };
            _database.WeekGameSetGames.Add(row);
            existingRows.Add(row);
            gameSetGameId = row.Id;
        }

        RecalculateLockAtUtc(set, existingRows);

        await PointValueRecalculator.RecalculateAsync(_database, leagueId, league.DefaultPointValue, existingRows, cancellationToken)
            .ConfigureAwait(false);

        WriteAudit(caller, AuditAction.GameManuallyAdded, gameSetGameId, new { week, gameId });

        _collector.Raise(new GameAddedToSet(leagueId, week, set.Id, [gameSetGameId], ReasonManual)
        {
            OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);

        return await GetWeekGameSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Manually removes a game. 409 when the week is already locked.</summary>
    public async Task<WeekGameSetResponse> RemoveGameAsync(
        Membership caller,
        int week,
        Guid gameId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Guid leagueId = caller.LeagueId;
        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken).ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new GameSetRuleViolation(GameSetRuleViolationCode.GameNotFound, "No such game in this week.");

        if (set.IsLocked)
        {
            throw new GameSetRuleViolation(GameSetRuleViolationCode.Locked, "This week is already locked.");
        }

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .Where(r => r.WeekGameSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        WeekGameSetGame row = rows.FirstOrDefault(r => r.GameId == gameId && !r.IsRemoved)
            ?? throw new GameSetRuleViolation(GameSetRuleViolationCode.GameNotFound, "No such active game in this week.");

        row.IsRemoved = true;
        row.RemovedReason = ReasonManual;

        RecalculateLockAtUtc(set, rows);

        WriteAudit(caller, AuditAction.GameManuallyRemoved, row.Id, new { week, gameId });

        _collector.Raise(new GameRemovedFromSet(leagueId, week, set.Id, row.Id, gameId, ReasonManual)
        {
            OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);

        return await GetWeekGameSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The week's active games, as every member sees them.</summary>
    public async Task<WeekGameSetResponse> GetWeekGameSetAsync(Guid leagueId, int week, CancellationToken cancellationToken)
    {
        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return new WeekGameSetResponse(week, null, null, false, false, []);
        }

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .AsNoTracking()
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.WeekGameSetId == set.Id && !row.IsRemoved)
            .OrderBy(row => row.Game!.KickoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> ranks = await BuildRankLookupAsync(league.SeasonYear, week, cancellationToken)
            .ConfigureAwait(false);

        GameSetGameDto[] dtos = [.. rows.Select(row => GameSetGameDtoMapper.Map(
            row.Id,
            row.Game!,
            ranks.TryGetValue(row.Game!.HomeTeamId, out int homeRank) ? homeRank : null,
            ranks.TryGetValue(row.Game!.AwayTeamId, out int awayRank) ? awayRank : null,
            row.ResolvedPointValue,
            league.DefaultPointValue,
            row.Source,
            row.IsVoided,
            row.ResultOverrideWinnerTeamId))];

        DateTimeOffset? lockAtUtc = set.LockAtUtc is DateTime lockAt ? new DateTimeOffset(lockAt, TimeSpan.Zero) : null;
        string? lockDisplay = lockAtUtc is DateTimeOffset value ? SeasonCalendar.EasternDisplay(value) : null;

        return new WeekGameSetResponse(week, lockAtUtc, lockDisplay, set.IsLocked, set.IsComplete, dtos);
    }

    /// <summary>
    /// Every game inside an already-locked week that has since been postponed or cancelled and
    /// has not yet been voided (Feature 02/06, P3-04): the "needs a decision" list P2-04's data
    /// page and P5-02's corrections flow both read. Optionally scoped to one league.
    /// </summary>
    public async Task<NeedsVoidReviewItem[]> ListNeedsVoidReviewAsync(Guid? leagueId, CancellationToken cancellationToken)
    {
        IQueryable<WeekGameSetGame> query = _database.WeekGameSetGames
            .AsNoTracking()
            .Include(row => row.WeekGameSet!.League)
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.WeekGameSet!.LockedUtc != null
                && !row.IsVoided
                && (row.Game!.Status == GameStatus.Postponed || row.Game!.Status == GameStatus.Cancelled));

        if (leagueId is Guid id)
        {
            query = query.Where(row => row.WeekGameSet!.LeagueId == id);
        }

        List<WeekGameSetGame> rows = await query
            .OrderBy(row => row.WeekGameSet!.LeagueId)
            .ThenBy(row => row.WeekGameSet!.Week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new NeedsVoidReviewItem(
            row.WeekGameSet!.LeagueId,
            row.WeekGameSet!.League?.Name ?? string.Empty,
            row.WeekGameSet!.Week,
            row.WeekGameSetId,
            row.Id,
            row.GameId,
            row.Game!.HomeTeam!.School,
            row.Game!.AwayTeam!.School,
            row.Game!.Status,
            new DateTimeOffset(row.Game!.KickoffUtc, TimeSpan.Zero)))];
    }

    /// <summary>Saturday FBS games for a season/week, for the manual-add candidate search.</summary>
    public async Task<GameCandidate[]> SearchCandidatesAsync(
        int seasonYear,
        int week,
        string? search,
        CancellationToken cancellationToken)
    {
        List<Game> games = await _database.Games
            .AsNoTracking()
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.SeasonYear == seasonYear
                && g.Week == week
                && g.IsSaturdayEastern
                && g.HomeTeam!.Classification == TeamClassification.Fbs
                && g.AwayTeam!.Classification == TeamClassification.Fbs
                && g.Status != GameStatus.Postponed
                && g.Status != GameStatus.Cancelled)
            .OrderBy(g => g.KickoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string needle = search.Trim();
            games = [.. games.Where(g =>
                Contains(g.HomeTeam!.School, needle)
                || Contains(g.HomeTeam!.Abbreviation, needle)
                || Contains(g.AwayTeam!.School, needle)
                || Contains(g.AwayTeam!.Abbreviation, needle))];
        }

        Dictionary<Guid, int> ranks = await BuildRankLookupAsync(seasonYear, week, cancellationToken).ConfigureAwait(false);

        return [.. games.Select(g => new GameCandidate(
            g.Id,
            GameSetGameDtoMapper.ToTeamDto(g.HomeTeam!),
            GameSetGameDtoMapper.ToTeamDto(g.AwayTeam!),
            ranks.TryGetValue(g.HomeTeamId, out int homeRank) ? homeRank : null,
            ranks.TryGetValue(g.AwayTeamId, out int awayRank) ? awayRank : null,
            new DateTimeOffset(g.KickoffUtc, TimeSpan.Zero),
            g.IsConferenceGame))];
    }

    /// <summary>The league's default game-set rules.</summary>
    public async Task<GameSetRuleDto[]> GetDefaultRulesAsync(Guid leagueId, CancellationToken cancellationToken) =>
        await LoadRuleDtosAsync(leagueId, week: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Full-replaces the league's default game-set rules. Save only: the caller must call
    /// <see cref="GenerateAsync"/> separately to apply the change to any week (03-API-Contracts.md).
    /// </summary>
    /// <exception cref="GameSetRuleConfigurationException">A rule is missing a required field or
    /// names a non-FBS conference/team.</exception>
    public async Task<GameSetRuleDto[]> ReplaceDefaultRulesAsync(
        Guid leagueId,
        IReadOnlyList<GameSetRuleDto> rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);

        await ValidateRulesAsync(rules, cancellationToken).ConfigureAwait(false);

        List<GameSetRule> existing = await _database.GameSetRules
            .Where(r => r.LeagueId == leagueId && r.Week == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _database.GameSetRules.RemoveRange(existing);

        InsertRules(leagueId, week: null, rules);

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetDefaultRulesAsync(leagueId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The week's rule override state, echoing the default rules read-only when unused.</summary>
    public async Task<WeekRulesResponse> GetWeekRulesAsync(Guid leagueId, int week, CancellationToken cancellationToken)
    {
        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken).ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false);

        if (set is null || !set.UsesOverride)
        {
            GameSetRuleDto[] defaults = await GetDefaultRulesAsync(leagueId, cancellationToken).ConfigureAwait(false);
            return new WeekRulesResponse(false, defaults);
        }

        GameSetRuleDto[] rules = await LoadRuleDtosAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
        return new WeekRulesResponse(true, rules);
    }

    /// <summary>
    /// Full-replaces a week's rule override, or clears it, then - only when <paramref name="week"/>
    /// is the league's current week (D-069, superseding D-065 for this one route) - immediately
    /// regenerates it through <see cref="GenerateAsync"/>, exactly as if the caller had made the
    /// two calls the commissioner UI does. Every other week is still save only, as D-065 says.
    /// 409 when the week is already locked.
    /// </summary>
    /// <exception cref="GameSetRuleConfigurationException">A rule is invalid (only checked when
    /// <paramref name="usesOverride"/> is true).</exception>
    /// <exception cref="GameSetRuleViolation">
    /// <see cref="GameSetRuleViolationCode.Locked"/> before anything is saved, or - only for the
    /// current week - <see cref="GameSetRuleViolationCode.ExceedsMax"/> from the regenerate that
    /// follows the save (the rules are saved either way; only the regeneration is refused).
    /// </exception>
    public async Task<WeekRulesResponse> ReplaceWeekRulesAsync(
        Guid leagueId,
        int week,
        bool usesOverride,
        IReadOnlyList<GameSetRuleDto> rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);

        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet set = await GetOrCreateWeekSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        if (set.IsLocked)
        {
            throw new GameSetRuleViolation(GameSetRuleViolationCode.Locked, "This week is already locked.");
        }

        if (usesOverride)
        {
            await ValidateRulesAsync(rules, cancellationToken).ConfigureAwait(false);
        }

        List<GameSetRule> existing = await _database.GameSetRules
            .Where(r => r.LeagueId == leagueId && r.Week == week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _database.GameSetRules.RemoveRange(existing);

        if (usesOverride)
        {
            InsertRules(leagueId, week, rules);
        }

        set.UsesOverride = usesOverride;

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (await IsCurrentWeekAsync(league, week, cancellationToken).ConfigureAwait(false))
        {
            await GenerateAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
        }

        return await GetWeekRulesAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True when <paramref name="week"/> is the league's current week right now (D-069): the
    /// week <see cref="SeasonCalendar.CurrentWeekAt"/> reports, clamped to the league's own
    /// <see cref="League.FirstWeek"/>/<see cref="League.LastWeek"/> range exactly like
    /// <c>RegenerateGameSetsJob</c>.
    /// </summary>
    private async Task<bool> IsCurrentWeekAsync(League league, int week, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> weeks = await _seasonWeekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        if (weeks.Count == 0)
        {
            return false;
        }

        int currentWeek = SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), weeks).Week;
        currentWeek = Math.Clamp(currentWeek, league.FirstWeek, league.LastWeek);
        return currentWeek == week;
    }

    private void InsertRules(Guid leagueId, int? week, IReadOnlyList<GameSetRuleDto> rules)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            GameSetRuleDto dto = rules[i];
            _database.GameSetRules.Add(new GameSetRule
            {
                Id = Guid.CreateVersion7(),
                LeagueId = leagueId,
                Week = week,
                RuleType = dto.RuleType,
                ConferenceId = dto.RuleType == GameSetRuleType.Conference ? dto.ConferenceId : null,
                TeamId = dto.RuleType == GameSetRuleType.Team ? dto.TeamId : null,
                ConferenceGamesOnly = dto.RuleType == GameSetRuleType.Conference && dto.ConferenceGamesOnly,
                SortOrder = i,
            });
        }
    }

    private async Task ValidateRulesAsync(IReadOnlyList<GameSetRuleDto> rules, CancellationToken cancellationToken)
    {
        Guid[] conferenceIds = [.. rules.Where(r => r.RuleType == GameSetRuleType.Conference && r.ConferenceId is not null)
            .Select(r => r.ConferenceId!.Value)
            .Distinct()];
        Guid[] teamIds = [.. rules.Where(r => r.RuleType == GameSetRuleType.Team && r.TeamId is not null)
            .Select(r => r.TeamId!.Value)
            .Distinct()];

        Dictionary<Guid, TeamClassification> conferences = conferenceIds.Length == 0
            ? []
            : await _database.Conferences
                .AsNoTracking()
                .Where(c => conferenceIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Classification, cancellationToken)
                .ConfigureAwait(false);

        Dictionary<Guid, TeamClassification> teams = teamIds.Length == 0
            ? []
            : await _database.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Classification, cancellationToken)
                .ConfigureAwait(false);

        var errors = new Dictionary<string, List<string>>();

        for (int i = 0; i < rules.Count; i++)
        {
            GameSetRuleDto rule = rules[i];

            switch (rule.RuleType)
            {
                case GameSetRuleType.Top25:
                    break;

                case GameSetRuleType.Conference:
                    if (rule.ConferenceId is not Guid conferenceId
                        || !conferences.TryGetValue(conferenceId, out TeamClassification conferenceClass)
                        || conferenceClass != TeamClassification.Fbs)
                    {
                        AddError(errors, i, nameof(GameSetRuleDto.ConferenceId), "A conference rule needs an FBS conference.");
                    }

                    break;

                case GameSetRuleType.Team:
                    if (rule.TeamId is not Guid teamId
                        || !teams.TryGetValue(teamId, out TeamClassification teamClass)
                        || teamClass != TeamClassification.Fbs)
                    {
                        AddError(errors, i, nameof(GameSetRuleDto.TeamId), "A team rule needs an FBS team.");
                    }

                    break;

                default:
                    AddError(errors, i, nameof(GameSetRuleDto.RuleType), $"{rule.RuleType} is not a game-set rule type.");
                    break;
            }
        }

        if (errors.Count > 0)
        {
            throw new GameSetRuleConfigurationException(
                errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, int index, string field, string message)
    {
        string key = $"Rules[{index}].{field}";
        if (!errors.TryGetValue(key, out List<string>? messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private async Task<GameSetRuleDto[]> LoadRuleDtosAsync(Guid leagueId, int? week, CancellationToken cancellationToken)
    {
        List<GameSetRule> rules = await _database.GameSetRules
            .AsNoTracking()
            .Include(r => r.Conference)
            .Include(r => r.Team)
            .Where(r => r.LeagueId == leagueId && r.Week == week)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rules.Select(r => new GameSetRuleDto(
            r.Id,
            r.RuleType,
            r.ConferenceId,
            r.Conference?.Name,
            r.TeamId,
            r.Team?.School,
            r.ConferenceGamesOnly,
            r.SortOrder))];
    }

    private static RuleInfo ToRuleInfo(GameSetRuleDto dto) => new(
        dto.RuleType,
        dto.RuleType == GameSetRuleType.Conference ? dto.ConferenceId : null,
        dto.RuleType == GameSetRuleType.Team ? dto.TeamId : null,
        dto.RuleType == GameSetRuleType.Conference && dto.ConferenceGamesOnly);

    private async Task<IReadOnlyList<RuleInfo>> LoadSavedRulesAsync(
        Guid leagueId,
        int week,
        bool usesOverride,
        CancellationToken cancellationToken)
    {
        int? ruleWeek = usesOverride ? week : null;

        List<GameSetRule> rules = await _database.GameSetRules
            .AsNoTracking()
            .Where(r => r.LeagueId == leagueId && r.Week == ruleWeek)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rules.Select(r => new RuleInfo(r.RuleType, r.ConferenceId, r.TeamId, r.ConferenceGamesOnly))];
    }

    private async Task<List<GameInfo>> LoadGameInfosAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        List<Game> games = await _database.Games
            .AsNoTracking()
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.SeasonYear == seasonYear && g.Week == week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. games.Select(g => new GameInfo(
            g.Id,
            g.HomeTeamId,
            g.AwayTeamId,
            g.HomeTeam!.ConferenceId,
            g.AwayTeam!.ConferenceId,
            g.HomeTeam!.Classification,
            g.AwayTeam!.Classification,
            g.KickoffUtc,
            g.IsSaturdayEastern,
            g.IsConferenceGame,
            g.Status))];
    }

    private async Task<List<RankingSet>> LoadRankingSetsAsync(int seasonYear, int uptoWeek, CancellationToken cancellationToken)
    {
        List<Ranking> rankings = await _database.Rankings
            .AsNoTracking()
            .Where(r => r.SeasonYear == seasonYear && r.Week <= uptoWeek && r.Poll == Ranking.ApPoll)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rankings
            .GroupBy(r => r.Week)
            .Select(g => new RankingSet(g.Key, g.Max(r => r.FetchedUtc), g.Select(r => r.TeamId).ToHashSet()))];
    }

    private async Task<Dictionary<Guid, int>> BuildRankLookupAsync(int seasonYear, int week, CancellationToken cancellationToken)
    {
        List<Ranking> current = await _database.Rankings
            .AsNoTracking()
            .Where(r => r.SeasonYear == seasonYear && r.Week == week && r.Poll == Ranking.ApPoll)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current.Count > 0)
        {
            return current.ToDictionary(r => r.TeamId, r => r.Rank);
        }

        int? priorWeek = await _database.Rankings
            .AsNoTracking()
            .Where(r => r.SeasonYear == seasonYear && r.Week < week && r.Poll == Ranking.ApPoll)
            .OrderByDescending(r => r.Week)
            .Select(r => (int?)r.Week)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (priorWeek is null)
        {
            return [];
        }

        List<Ranking> prior = await _database.Rankings
            .AsNoTracking()
            .Where(r => r.SeasonYear == seasonYear && r.Week == priorWeek && r.Poll == Ranking.ApPoll)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return prior.ToDictionary(r => r.TeamId, r => r.Rank);
    }

    private async Task<IReadOnlyList<ExistingSetGame>> LoadExistingSetGamesAsync(Guid weekGameSetId, CancellationToken cancellationToken)
    {
        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == weekGameSetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new ExistingSetGame(row.GameId, row.Source, row.IsRemoved))];
    }

    private async Task<GameSetGameDto[]> BuildDtosAsync(
        Guid leagueId,
        League league,
        int week,
        IReadOnlyList<GeneratedGame> selected,
        IReadOnlyList<GameInfo> games,
        Dictionary<Guid, int?> overridesByGameId,
        CancellationToken cancellationToken)
    {
        if (selected.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, GameInfo> pool = games.ToDictionary(g => g.GameId);
        Guid[] gameIds = [.. selected.Select(g => g.GameId)];

        List<Game> loadedGames = await _database.Games
            .AsNoTracking()
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => gameIds.Contains(g.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, Game> gamesById = loadedGames.ToDictionary(g => g.Id);

        IReadOnlyList<Domain.Points.PointRuleInfo> pointRules = await PointValueRecalculator
            .LoadPointRuleInfosAsync(_database, leagueId, cancellationToken)
            .ConfigureAwait(false);

        List<GameLine> lines = await _database.GameLines
            .AsNoTracking()
            .Where(line => gameIds.Contains(line.GameId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, decimal> spreads = lines
            .GroupBy(line => line.GameId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(line => line.FetchedUtc).First().Spread);

        Dictionary<Guid, int> ranks = await BuildRankLookupAsync(league.SeasonYear, week, cancellationToken)
            .ConfigureAwait(false);

        var results = new GameSetGameDto[selected.Count];
        for (int i = 0; i < selected.Count; i++)
        {
            GeneratedGame generated = selected[i];
            Game game = gamesById[generated.GameId];
            GameInfo info = pool[generated.GameId];

            var pointGameInfo = new Domain.Points.PointGameInfo(
                info.HomeTeamId,
                info.AwayTeamId,
                info.HomeConferenceId,
                info.AwayConferenceId,
                info.IsConferenceGame);

            int? pointValueOverride = overridesByGameId.TryGetValue(generated.GameId, out int? overrideValue)
                ? overrideValue
                : null;

            decimal? spread = spreads.TryGetValue(generated.GameId, out decimal spreadValue) ? spreadValue : null;

            Domain.Points.PointResolution resolution = Domain.Points.PointValueResolver.Resolve(
                pointGameInfo,
                pointValueOverride,
                league.DefaultPointValue,
                pointRules,
                spread);

            results[i] = GameSetGameDtoMapper.Map(
                null,
                game,
                ranks.TryGetValue(info.HomeTeamId, out int homeRank) ? homeRank : null,
                ranks.TryGetValue(info.AwayTeamId, out int awayRank) ? awayRank : null,
                resolution.Value,
                league.DefaultPointValue,
                generated.Source,
                isVoided: false,
                resultOverrideWinnerTeamId: null);
        }

        return results;
    }

    private static void RecalculateLockAtUtc(WeekGameSet set, IReadOnlyList<WeekGameSetGame> rows)
    {
        DateTime? earliest = rows
            .Where(r => !r.IsRemoved && r.Game is not null)
            .Select(r => r.Game!.KickoffUtc)
            .Cast<DateTime?>()
            .Min();

        set.LockAtUtc = earliest;
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private void WriteAudit(Membership actor, AuditAction action, Guid? targetId, object details)
    {
        _database.AuditLog.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            LeagueId = actor.LeagueId,
            ActorMembershipId = actor.Id,
            Action = action,
            TargetId = targetId,
            Details = JsonSerializer.Serialize(details),
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }
}
