using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.TeamForm;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Contracts.TeamForm;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// The picks page's recent form tray (Feature 17, D-194): each set team's last games, computed on
/// read by <see cref="TeamFormCalculator"/> from <c>Games</c>, which already holds every game
/// involving an FBS team with its final score whether or not any league picked it.
/// </summary>
/// <remarks>
/// The week's games come from <see cref="GameSetService.GetWeekGameSetAsync"/> so the range guard
/// and the picks-open rule are the ones the picks page answers to.
/// </remarks>
public sealed class TeamFormService
{
    private readonly AppDbContext _database;
    private readonly GameSetService _gameSetService;

    /// <summary>Creates the service.</summary>
    public TeamFormService(AppDbContext database, GameSetService gameSetService)
    {
        _database = database;
        _gameSetService = gameSetService;
    }

    /// <summary>Every team in the week's set with its last games before its game this week.</summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="week">The week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.GameSets.GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task<WeekTeamFormResponse> GetWeekTeamFormAsync(
        Guid leagueId,
        int week,
        CancellationToken cancellationToken)
    {
        WeekGameSetResponse set = await _gameSetService
            .GetWeekGameSetAsync(leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        // Same gate as PickService.GetMyPicksAsync: before picks open the set is a preview (D-192).
        if (set.Games.Length == 0 || (!set.PicksOpen && !set.IsLocked))
        {
            return new WeekTeamFormResponse(week, []);
        }

        // A team plays once a week; the earliest kickoff is only a guard for a malformed set.
        Dictionary<Guid, DateTime> kickoffByTeam = set.Games
            .SelectMany(game => new[] { (game.HomeTeam.TeamId, game.KickoffUtc), (game.AwayTeam.TeamId, game.KickoffUtc) })
            .GroupBy(pair => pair.TeamId)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.KickoffUtc).UtcDateTime);

        Guid[] teamIds = [.. kickoffByTeam.Keys];
        DateTime latestKickoff = kickoffByTeam.Values.Max();

        int seasonYear = await _database.Leagues
            .Where(league => league.Id == leagueId)
            .Select(league => league.SeasonYear)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        List<FormGame> games = await _database.Games
            .AsNoTracking()
            .Where(game => game.SeasonYear == seasonYear
                && game.Status == GameStatus.Final
                && game.HomeScore != null
                && game.AwayScore != null
                && game.KickoffUtc < latestKickoff
                && (teamIds.Contains(game.HomeTeamId) || teamIds.Contains(game.AwayTeamId)))
            .Select(game => new FormGame(
                game.Id, game.HomeTeamId, game.AwayTeamId, game.KickoffUtc, game.HomeScore!.Value, game.AwayScore!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, FormEntry[]> formByTeam = kickoffByTeam.ToDictionary(
            pair => pair.Key,
            pair => TeamFormCalculator.LastGames(pair.Key, pair.Value, games));

        Guid[] opponentIds = [.. formByTeam.Values.SelectMany(entries => entries).Select(entry => entry.OpponentTeamId).Distinct()];
        Dictionary<Guid, TeamDto> opponents = (await _database.Teams
                .AsNoTracking()
                .Where(team => opponentIds.Contains(team.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(team => team.Id, GameSetGameDtoMapper.ToTeamDto);

        TeamFormDto[] teams =
        [
            .. formByTeam.Select(pair => new TeamFormDto(
                pair.Key,
                [.. pair.Value.Select(entry => new RecentGameDto(
                    entry.GameId,
                    opponents[entry.OpponentTeamId],
                    entry.IsHome,
                    entry.TeamScore,
                    entry.OpponentScore,
                    entry.Result,
                    new DateTimeOffset(entry.KickoffUtc, TimeSpan.Zero)))])),
        ];

        return new WeekTeamFormResponse(week, teams);
    }
}
