using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Dashboard;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for the influence dashboard (Feature 05, P6-02): a member's own dashboard
/// for one week, before and after lock. <see cref="InfluenceCalculator"/> owns the algorithm; this
/// class owns persistence, the "has the lock job run yet" gate, and the DTO shapes.
/// </summary>
/// <remarks>
/// <para>
/// "Locked" here means <c>WeekGameSets.LockedUtc != null</c> - the lock job has actually run -
/// not merely that <c>LockAtUtc</c> has passed (D-1xx, see <c>DECISIONS.md</c>). Point values are
/// frozen and every member's final status is settled only once the job has run, so the dashboard
/// waits for it rather than reusing <c>PickService</c>'s "reached the instant" guard.
/// </para>
/// <para>
/// A week in the league's range with no game set yet behaves exactly like an unlocked one:
/// <see cref="DashboardResponse.IsAvailable"/> false, <see cref="DashboardResponse.LockAtUtc"/>
/// null - <see cref="GameSetService.GetWeekGameSetAsync"/> already answers that shape for an empty
/// set, so no special case is needed here.
/// </para>
/// </remarks>
public sealed class DashboardService
{
    private readonly AppDbContext _database;
    private readonly GameSetService _gameSetService;
    private readonly ILiveScoreHealth _liveScoreHealth;

    /// <summary>Creates the service.</summary>
    public DashboardService(
        AppDbContext database,
        GameSetService gameSetService,
        ILiveScoreHealth liveScoreHealth)
    {
        _database = database;
        _gameSetService = gameSetService;
        _liveScoreHealth = liveScoreHealth;
    }

    /// <summary>
    /// The caller's own influence dashboard for a week. Never anyone else's - the story rules out
    /// viewing it "as" another member, so there is no parameter for that.
    /// </summary>
    /// <param name="viewer">The caller's membership, from <c>HttpContext.GetMembership()</c>.</param>
    /// <param name="week">The week to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task<DashboardResponse> GetAsync(
        Membership viewer,
        int week,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        // One query: reuses the same games-with-teams-and-ranks projection every other endpoint
        // renders (GameSetGameDtoMapper). Also runs the week-in-range guard.
        WeekGameSetResponse games = await _gameSetService
            .GetWeekGameSetAsync(viewer.LeagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.LeagueId == viewer.LeagueId && candidate.Week == week,
                cancellationToken)
            .ConfigureAwait(false);

        if (set?.LockedUtc is null)
        {
            return new DashboardResponse(false, games.LockAtUtc, games.LockAtEasternDisplay, 0, 0, false, [], []);
        }

        Dictionary<Guid, GameSetGameDto> gamesById = games.Games
            .Where(game => game.GameSetGameId is not null)
            .ToDictionary(game => game.GameSetGameId!.Value);

        InfluenceGame[] influenceGames = [.. games.Games.Select(ToInfluenceGame)];

        // One query: every member active at lock is one with a WeekSubmissions row for this set -
        // that single condition includes a member removed since lock and excludes one who joined
        // after it (AGENT-NOTES.md, "Dashboard").
        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => _database.WeekSubmissions
                .Any(submission => submission.WeekGameSetId == set.Id && submission.MembershipId == membership.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        InfluenceMember[] membersActiveAtLock = [.. memberships
            .Select(membership => new InfluenceMember(
                membership.Id, MemberNameProjection.Effective(membership), membership.RemovedUtc is not null))
            .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)];

        // One query: every pick on a row belonging to this set. IndexPicks in the calculator
        // ignores anything on a membership or game it was not handed, so a pick on a removed row
        // or from a post-lock joiner is dropped there rather than filtered again here.
        InfluencePick[] picks = [.. (await _database.Picks
            .AsNoTracking()
            .Where(pick => pick.WeekGameSetGame!.WeekGameSetId == set.Id)
            .Select(pick => new { pick.MembershipId, pick.WeekGameSetGameId, pick.PickedTeamId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(pick => new InfluencePick(pick.MembershipId, pick.WeekGameSetGameId, pick.PickedTeamId))];

        InfluenceResult result = InfluenceCalculator.Calculate(new InfluenceRequest(
            viewer.Id,
            influenceGames,
            membersActiveAtLock,
            picks));

        return new DashboardResponse(
            true,
            games.LockAtUtc,
            games.LockAtEasternDisplay,
            result.PointsSoFar,
            result.MaxRemaining,
            _liveScoreHealth.ScoresMayBeStale,
            [.. result.Games.Select(entry => ToDto(entry, gamesById))],
            [.. result.EveryoneAgrees.Select(entry => ToDto(entry, gamesById))]);
    }

    /// <summary>
    /// The DTO already carries <see cref="GameSetGameDto.WinnerTeamId"/> - <see cref="WinnerResolver"/>'s
    /// answer from the override (when set) or the score (once Final) - so passing it straight
    /// through as the "override" reproduces the identical answer without a second raw column:
    /// when it is non-null <see cref="WinnerResolver"/> returns it unchanged, and when it is null
    /// the same status/score fields decide the winner exactly as they did the first time.
    /// </summary>
    private static InfluenceGame ToInfluenceGame(GameSetGameDto game) => new(
        game.GameSetGameId!.Value,
        game.GameId,
        game.HomeTeam.TeamId,
        game.AwayTeam.TeamId,
        game.KickoffUtc.UtcDateTime,
        game.PointValue,
        game.Status,
        game.HomeScore,
        game.AwayScore,
        game.WinnerTeamId,
        game.IsVoided);

    private static DashboardGameDto ToDto(InfluenceGameResult result, Dictionary<Guid, GameSetGameDto> gamesById) =>
        new(
            gamesById[result.GameSetGameId],
            result.MyTeamId,
            result.MyOutcome,
            result.OppositeCount,
            [.. result.OppositePicks.Select(ToMemberRef)],
            [.. result.NoPick.Select(ToMemberRef)],
            [.. result.HomePickers.Select(ToMemberRef)],
            [.. result.AwayPickers.Select(ToMemberRef)],
            result.SwingPoints);

    private static MemberRef ToMemberRef(InfluenceMember member) =>
        new(member.MembershipId, member.DisplayName, member.IsFormer);
}
