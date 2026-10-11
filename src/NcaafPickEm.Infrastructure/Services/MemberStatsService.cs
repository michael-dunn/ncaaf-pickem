using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Stats;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Stats;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for member stats (Feature 14): the league stats page and one member's
/// profile. <see cref="MemberStatsCalculator"/> owns every rule; this class owns the queries.
/// </summary>
/// <remarks>
/// Stats are computed on read from the rows scoring already keeps true - the locked sets' games,
/// picks and <c>WeekResults</c> - so a game going final, an override or a void shows up on the next
/// read with no table of its own to refresh (Feature 14, "Freshness"). Each read is a fixed four
/// queries whatever the league's size.
/// </remarks>
public sealed class MemberStatsService
{
    private readonly AppDbContext _database;

    /// <summary>Creates the service.</summary>
    /// <param name="database">The database.</param>
    public MemberStatsService(AppDbContext database)
    {
        _database = database;
    }

    /// <summary>The league stats page: every active member ranked in each category.</summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<LeagueStats> GetLeagueStatsAsync(
        Guid leagueId,
        Guid viewerMembershipId,
        CancellationToken cancellationToken)
    {
        Inputs inputs = await LoadAsync(leagueId, cancellationToken).ConfigureAwait(false);

        return MemberStatsCalculator.LeagueStats(
            inputs.ActiveMembers, inputs.Games, inputs.Picks, inputs.Results, viewerMembershipId);
    }

    /// <summary>One member's stats profile; former members of the league included.</summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="membershipId">The member to profile.</param>
    /// <param name="viewerMembershipId">The caller's membership, for <c>IsMe</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="LeagueRuleViolation">No such membership in this league (404).</exception>
    public async Task<MemberStatsProfile> GetMemberProfileAsync(
        Guid leagueId,
        Guid membershipId,
        Guid viewerMembershipId,
        CancellationToken cancellationToken)
    {
        Inputs inputs = await LoadAsync(leagueId, cancellationToken).ConfigureAwait(false);

        StandingsMember member = inputs.AllMembers.FirstOrDefault(candidate => candidate.MembershipId == membershipId)
            ?? throw new LeagueRuleViolation(
                LeagueRuleViolationCode.MembershipNotFound,
                "That member is not in this league.");

        return MemberStatsCalculator.Profile(
            member, inputs.ActiveMembers, inputs.Games, inputs.Picks, inputs.Results, viewerMembershipId);
    }

    private async Task<Inputs> LoadAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        StandingsMember[] allMembers =
        [
            .. memberships.Select(membership => new StandingsMember(
                membership.Id,
                MemberNameProjection.Effective(membership),
                IsFormer: !membership.IsActive,
                membership.JoinedWeek))
        ];

        // Active rows of locked sets only: a removed or voided game never counts (Feature 14,
        // "Counted game"), and the winner comes from WinnerResolver like every other reader.
        var rows = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSet!.LeagueId == leagueId
                && row.WeekGameSet.LockedUtc != null
                && !row.IsRemoved
                && !row.IsVoided)
            .Select(row => new
            {
                row.Id,
                row.WeekGameSet!.Week,
                row.WeekGameSet.IsComplete,
                row.Game!.HomeTeamId,
                row.Game.AwayTeamId,
                row.Game.Status,
                row.Game.HomeScore,
                row.Game.AwayScore,
                row.SpreadAtLock,
                row.ResultOverrideWinnerTeamId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        StatsGame[] games =
        [
            .. rows.Select(row => new StatsGame(
                row.Id,
                row.Week,
                row.HomeTeamId,
                row.AwayTeamId,
                row.SpreadAtLock,
                WinnerResolver.Resolve(
                    row.ResultOverrideWinnerTeamId,
                    row.Status,
                    row.HomeScore,
                    row.AwayScore,
                    row.HomeTeamId,
                    row.AwayTeamId),
                row.IsComplete))
        ];

        StatsPick[] picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => pick.WeekGameSetGame!.WeekGameSet!.LeagueId == leagueId
                && pick.WeekGameSetGame.WeekGameSet.LockedUtc != null)
            .Select(pick => new StatsPick(pick.MembershipId, pick.WeekGameSetGameId, pick.PickedTeamId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

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

        return new Inputs(
            allMembers,
            [.. allMembers.Where(member => !member.IsFormer)],
            games,
            picks,
            results);
    }

    private sealed record Inputs(
        StandingsMember[] AllMembers,
        StandingsMember[] ActiveMembers,
        StatsGame[] Games,
        StatsPick[] Picks,
        StandingsWeekResult[] Results);
}
