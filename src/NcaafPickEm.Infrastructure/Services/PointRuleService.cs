using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for point-value configuration (Feature 03, P3-03): saves the league's
/// point rules, default, and per-game overrides, and re-resolves every unlocked week whenever any
/// of them change.
/// </summary>
public sealed class PointRuleService
{
    private readonly AppDbContext _database;

    /// <summary>Creates the service.</summary>
    public PointRuleService(AppDbContext database)
    {
        _database = database;
    }

    /// <summary>The league's point rules, ordered by priority.</summary>
    public async Task<PointRuleDto[]> GetRulesAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        List<PointRule> rules = await _database.PointRules
            .AsNoTracking()
            .Where(rule => rule.LeagueId == leagueId)
            .OrderBy(rule => rule.Priority)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rules.Select(ToDto)];
    }

    /// <summary>
    /// Full-replaces the league's point rules and re-resolves every unlocked week's point
    /// values against the new configuration.
    /// </summary>
    /// <exception cref="PointRuleValidationException">
    /// A rule's point value is out of range, a priority repeats, or a rule is missing the field
    /// its type needs.
    /// </exception>
    public async Task<PointRuleDto[]> ReplaceRulesAsync(
        Guid leagueId,
        IReadOnlyList<PointRuleDto> rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);

        League league = await _database.Leagues
            .AsNoTracking()
            .FirstAsync(l => l.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PointRuleInfo> candidateInfos = [.. rules.Select(ToInfo)];
        IReadOnlyList<PointRuleError> errors = PointRuleValidation.Validate(candidateInfos, league.DefaultPointValue);

        if (errors.Count > 0)
        {
            throw new PointRuleValidationException(errors);
        }

        List<PointRule> existing = await _database.PointRules
            .Where(rule => rule.LeagueId == leagueId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _database.PointRules.RemoveRange(existing);

        foreach (PointRuleDto dto in rules)
        {
            _database.PointRules.Add(new PointRule
            {
                Id = Guid.CreateVersion7(),
                LeagueId = leagueId,
                Priority = dto.Priority,
                RuleType = dto.RuleType,
                ConferenceId = dto.RuleType == Shared.Enums.PointRuleType.ConferenceGame ? dto.ConferenceId : null,
                TeamId = dto.RuleType == Shared.Enums.PointRuleType.Team ? dto.TeamId : null,
                SpreadThreshold = dto.RuleType == Shared.Enums.PointRuleType.CloseSpread ? dto.SpreadThreshold : null,
                PointValue = dto.PointValue,
            });
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ReResolveUnlockedWeeksAsync(leagueId, cancellationToken).ConfigureAwait(false);

        return await GetRulesAsync(leagueId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets or clears a commissioner's manual point value for one game. 409 when the week is
    /// already locked.
    /// </summary>
    public async Task<GameSetGameDto> SetOverrideAsync(
        Guid leagueId,
        int week,
        Guid gameId,
        int? pointValue,
        CancellationToken cancellationToken)
    {
        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new GameSetRuleViolation(GameSetRuleViolationCode.GameNotFound, "No game set for this week yet.");

        if (set.IsLocked)
        {
            throw new GameSetRuleViolation(GameSetRuleViolationCode.Locked, "This week is already locked.");
        }

        WeekGameSetGame row = await _database.WeekGameSetGames
            .Include(r => r.Game!.HomeTeam)
            .Include(r => r.Game!.AwayTeam)
            .FirstOrDefaultAsync(r => r.WeekGameSetId == set.Id && r.GameId == gameId && !r.IsRemoved, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new GameSetRuleViolation(GameSetRuleViolationCode.GameNotFound, "No such active game in this week.");

        row.PointValueOverride = pointValue;

        await PointValueRecalculator.RecalculateAsync(_database, leagueId, league.DefaultPointValue, [row], cancellationToken)
            .ConfigureAwait(false);

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, int> ranks = await BuildRankLookupAsync(league.SeasonYear, week, cancellationToken)
            .ConfigureAwait(false);

        return GameSetGameDtoMapper.Map(
            row.Id,
            row.Game!,
            ranks.TryGetValue(row.Game!.HomeTeamId, out int homeRank) ? homeRank : null,
            ranks.TryGetValue(row.Game!.AwayTeamId, out int awayRank) ? awayRank : null,
            row.ResolvedPointValue,
            league.DefaultPointValue,
            row.Source,
            row.IsVoided,
            row.ResultOverrideWinnerTeamId);
    }

    /// <summary>
    /// Recomputes <c>ResolvedPointValue</c> for every active game in every unlocked week of the
    /// league, against its current rules and default. Called after a rules/default change; never
    /// touches a locked week.
    /// </summary>
    public async Task ReResolveUnlockedWeeksAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        League league = await _database.Leagues
            .AsNoTracking()
            .FirstAsync(l => l.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);

        List<WeekGameSet> unlockedSets = await _database.WeekGameSets
            .Where(set => set.LeagueId == leagueId && set.LockedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (unlockedSets.Count == 0)
        {
            return;
        }

        Guid[] setIds = [.. unlockedSets.Select(s => s.Id)];

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => setIds.Contains(row.WeekGameSetId) && !row.IsRemoved && !row.IsVoided)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return;
        }

        await PointValueRecalculator.RecalculateAsync(_database, leagueId, league.DefaultPointValue, rows, cancellationToken)
            .ConfigureAwait(false);

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private static PointRuleDto ToDto(PointRule rule) => new(
        rule.Id,
        rule.Priority,
        rule.RuleType,
        rule.ConferenceId,
        rule.TeamId,
        rule.SpreadThreshold,
        rule.PointValue);

    private static PointRuleInfo ToInfo(PointRuleDto dto) => new(
        dto.Priority,
        dto.RuleType,
        dto.ConferenceId,
        dto.TeamId,
        dto.SpreadThreshold,
        dto.PointValue,
        dto.RuleId);
}
