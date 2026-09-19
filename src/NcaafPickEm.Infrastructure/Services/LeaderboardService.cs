using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for the leaderboards (Feature 07, P5-03): the season standings, one week's
/// standings, and the post-lock picks grid. <see cref="StandingsCalculator"/> owns every rule;
/// this class owns the queries and hands the calculator flattened rows.
/// </summary>
/// <remarks>
/// Each read is a fixed handful of queries whatever the league's size - the season leaderboard is
/// three (memberships, results, snapshots) - because Feature 07 asks for it to load in under a
/// second for 50 members across 15 weeks. Games always come from
/// <see cref="GameSetService.GetWeekGameSetAsync"/>, so ranks, point values and
/// <c>WinnerTeamId</c> keep coming out of <see cref="GameSetGameDtoMapper"/> alone.
/// </remarks>
public sealed class LeaderboardService
{
    private readonly AppDbContext _database;
    private readonly GameSetService _gameSetService;

    /// <summary>Creates the service.</summary>
    /// <param name="database">The database.</param>
    /// <param name="gameSetService">Source of the week's games, for the grid.</param>
    public LeaderboardService(AppDbContext database, GameSetService gameSetService)
    {
        _database = database;
        _gameSetService = gameSetService;
    }

    /// <summary>
    /// The season leaderboard: active members only, ranked by total points, with points behind,
    /// weekly wins and a trend arrow.
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The standings. <see cref="SeasonLeaderboard.ThroughWeek"/> is the latest week with any
    /// result row, and null before the first week is scored.
    /// </returns>
    public async Task<SeasonLeaderboard> GetSeasonAsync(
        Guid leagueId,
        Guid viewerMembershipId,
        CancellationToken cancellationToken)
    {
        StandingsMember[] members = await LoadActiveMembersAsync(leagueId, cancellationToken).ConfigureAwait(false);

        StandingsWeekResult[] results = await _database.WeekResults
            .AsNoTracking()
            .Where(result => result.WeekGameSet!.LeagueId == leagueId)
            .Select(result => new StandingsWeekResult(
                result.MembershipId,
                result.WeekGameSet!.Week,
                result.Points,
                result.CorrectCount,
                result.ActiveGameCount,
                result.IsWeekComplete))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        StandingsSnapshot[] snapshots = await _database.SeasonStandingsSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.LeagueId == leagueId)
            .Select(snapshot => new StandingsSnapshot(
                snapshot.ThroughWeek,
                snapshot.MembershipId,
                snapshot.Rank,
                snapshot.TotalPoints))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        int? throughWeek = results.Length == 0 ? null : results.Max(result => result.Week);

        return new SeasonLeaderboard(
            throughWeek,
            StandingsCalculator.SeasonRows(members, results, snapshots, viewerMembershipId));
    }

    /// <summary>
    /// One week's leaderboard: everybody with a result row for the week, former members included.
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="week">The week.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task<WeekLeaderboard> GetWeekAsync(
        Guid leagueId,
        int week,
        Guid viewerMembershipId,
        CancellationToken cancellationToken)
    {
        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            return new WeekLeaderboard(week, IsComplete: false, []);
        }

        StandingsWeekResult[] results = await _database.WeekResults
            .AsNoTracking()
            .Where(result => result.WeekGameSetId == set.Id)
            .Select(result => new StandingsWeekResult(
                result.MembershipId,
                week,
                result.Points,
                result.CorrectCount,
                result.ActiveGameCount,
                result.IsWeekComplete))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // Everyone with a result row, whether or not they are still in the league: a former member
        // keeps the weeks they played (Feature 07, "Former members").
        Guid[] membershipIds = [.. results.Select(result => result.MembershipId)];
        StandingsMember[] members = await LoadMembersAsync(membershipIds, cancellationToken).ConfigureAwait(false);

        return new WeekLeaderboard(
            week,
            set.IsComplete,
            StandingsCalculator.WeekRows(week, members, results, set.IsComplete, viewerMembershipId));
    }

    /// <summary>
    /// The week's picks grid: games as rows, the members who were in the league at lock as columns,
    /// one cell each. 403 until the week locks, exactly like <c>GET .../picks</c>.
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="week">The week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    /// <exception cref="PickRuleViolation">The week has not locked yet (403).</exception>
    public async Task<WeekGrid> GetGridAsync(Guid leagueId, int week, CancellationToken cancellationToken)
    {
        // Runs the week-range guard itself, and derives WinnerTeamId through WinnerResolver.
        WeekGameSetResponse games = await _gameSetService
            .GetWeekGameSetAsync(leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        if (set?.LockedUtc is null)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.PicksNotVisible,
                "Everyone's picks become visible when the week locks.");
        }

        // Who was in the league at lock decides whose column appears - not the current roster, so
        // members removed since are still here, flagged. That means a WeekSubmissions row whose
        // status is Locked or Incomplete, the only two LockWeekJob writes (D-135): PickService
        // creates a row the moment a member first touches the week and D-111 leaves it behind when
        // the locker drops the membership, so "any row at all" would also column a member who left
        // before the week locked.
        Guid[] membershipIds = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == set.Id
                && (submission.Status == SubmissionStatus.Locked
                    || submission.Status == SubmissionStatus.Incomplete))
            .Select(submission => submission.MembershipId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        StandingsMember[] members = await LoadMembersAsync(membershipIds, cancellationToken).ConfigureAwait(false);

        // Voided games stay in the grid, greyed out (Feature 07, Week drill-down).
        Guid[] gameSetGameIds = [.. games.Games.Select(game => game.GameSetGameId!.Value)];

        GridPick[] picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => gameSetGameIds.Contains(pick.WeekGameSetGameId)
                && membershipIds.Contains(pick.MembershipId))
            .Select(pick => new GridPick(pick.MembershipId, pick.WeekGameSetGameId, pick.PickedTeamId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        GridGame[] gridGames =
        [
            .. games.Games.Select(game => new GridGame(
                game.GameSetGameId!.Value,
                game.IsVoided,
                game.WinnerTeamId))
        ];

        GridMember[] columns =
        [
            .. members.Select(member => new GridMember(member.MembershipId, member.DisplayName, member.IsFormer))
        ];

        return new WeekGrid(
            games.Games,
            columns,
            StandingsCalculator.GridCells(gridGames, members, picks));
    }

    private Task<WeekGameSet?> LoadSetAsync(Guid leagueId, int week, CancellationToken cancellationToken) =>
        _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.LeagueId == leagueId && set.Week == week, cancellationToken);

    private async Task<StandingsMember[]> LoadActiveMembersAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId && membership.RemovedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. memberships.Select(ToStandingsMember)];
    }

    private async Task<StandingsMember[]> LoadMembersAsync(Guid[] membershipIds, CancellationToken cancellationToken)
    {
        if (membershipIds.Length == 0)
        {
            return [];
        }

        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membershipIds.Contains(membership.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Grid columns render in this order, so it has to be stable and human: by effective name,
        // the same order GET .../picks uses for its member rows.
        return
        [
            .. memberships
                .Select(ToStandingsMember)
                .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.MembershipId)
        ];
    }

    private static StandingsMember ToStandingsMember(Membership membership) => new(
        membership.Id,
        MemberNameProjection.Effective(membership),
        IsFormer: !membership.IsActive,
        membership.JoinedWeek);
}
