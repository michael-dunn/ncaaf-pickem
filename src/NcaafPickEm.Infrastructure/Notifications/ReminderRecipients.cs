using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Query helper shared by the two Friday jobs and the Saturday one-shot (Feature 11, section 11):
/// which league/week is "current" right now, and who on it has not submitted.
/// </summary>
/// <remarks>
/// Every query here reads at send time, per the card ("recipients evaluated at send time") — none
/// of it is precomputed or cached, so a member who submits between the job being scheduled and it
/// actually running is correctly skipped.
/// </remarks>
public static class ReminderRecipients
{
    /// <summary>A league's unlocked game set for whatever week is current right now.</summary>
    /// <param name="LeagueId">The league.</param>
    /// <param name="Week">The current week.</param>
    /// <param name="Set">The set, guaranteed unlocked and with at least one active game.</param>
    public sealed record CurrentSet(Guid LeagueId, int Week, WeekGameSet Set);

    /// <summary>A member who has not submitted, with the name a notification should use.</summary>
    /// <param name="MembershipId">The membership.</param>
    /// <param name="UserId">Who to push to.</param>
    /// <param name="Name">Effective per-league display name (<see cref="MemberNameProjection"/>).</param>
    public sealed record UnsubmittedMember(Guid MembershipId, Guid UserId, string Name);

    /// <summary>
    /// Every league's current-week game set, skipping a league with no set for that week, a
    /// locked week, and a week with no active games (nothing to remind anyone about).
    /// </summary>
    /// <param name="database">The context.</param>
    /// <param name="weekSource">Resolves each league's season calendar.</param>
    /// <param name="nowUtc">The instant "current week" is evaluated at.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<IReadOnlyList<CurrentSet>> CurrentUnlockedSetsAsync(
        AppDbContext database,
        ISeasonWeekSource weekSource,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(weekSource);

        List<League> leagues = await database.Leagues.AsNoTracking().ToListAsync(cancellationToken);
        var nowOffset = new DateTimeOffset(nowUtc, TimeSpan.Zero);
        var results = new List<CurrentSet>();

        foreach (IGrouping<int, League> group in leagues.GroupBy(league => league.SeasonYear))
        {
            IReadOnlyList<SeasonWeek> weeks = await weekSource.GetWeeksAsync(group.Key, cancellationToken);
            if (weeks.Count == 0)
            {
                continue;
            }

            CurrentWeek current = SeasonCalendar.CurrentWeekAt(nowOffset, weeks);

            if (current.State != SeasonState.InSeason)
            {
                // Off-season guard (P8-01, D-191). Outside a week window CurrentWeekAt clamps to
                // the first or last week, so a league whose final week was never locked would keep
                // drawing a Friday reminder every week of the summer.
                continue;
            }

            int week = current.Week;

            foreach (League league in group)
            {
                WeekGameSet? set = await database.WeekGameSets.AsNoTracking()
                    .FirstOrDefaultAsync(
                        s => s.LeagueId == league.Id && s.Week == week,
                        cancellationToken);

                if (set is null || set.LockedUtc is not null || set.LockAtUtc is null)
                {
                    // No set generated yet, already locked, or nothing active to pick — the card's
                    // "no game set / already locked" skip cases, plus the degenerate case of a set
                    // with no active games (LockAtUtc is only null then).
                    continue;
                }

                results.Add(new CurrentSet(league.Id, week, set));
            }
        }

        return results;
    }

    /// <summary>
    /// Active members of <paramref name="leagueId"/> whose status on <paramref name="weekGameSetId"/>
    /// is not <see cref="SubmissionStatus.Submitted"/>. A member with no <c>WeekSubmissions</c> row
    /// counts as <see cref="SubmissionStatus.NotStarted"/>, per Feature 04.
    /// </summary>
    public static async Task<List<UnsubmittedMember>> UnsubmittedMembersAsync(
        AppDbContext database,
        Guid leagueId,
        Guid weekGameSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        var rows = await database.Memberships
            .AsNoTracking()
            .Where(m => m.LeagueId == leagueId && m.RemovedUtc == null)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                Name = m.DisplayNameOverride ?? (m.User != null ? m.User.DisplayName : string.Empty),
                Status = database.WeekSubmissions
                    .Where(ws => ws.MembershipId == m.Id && ws.WeekGameSetId == weekGameSetId)
                    .Select(ws => (SubmissionStatus?)ws.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => row.Status != SubmissionStatus.Submitted)
            .Select(row => new UnsubmittedMember(row.Id, row.UserId, row.Name))
            .ToList();
    }

    /// <summary>
    /// How many active games in <paramref name="weekGameSetId"/> <paramref name="membershipId"/>
    /// has not yet picked — the "N picks left" the member-facing reminders quote.
    /// </summary>
    public static async Task<int> PicksLeftAsync(
        AppDbContext database,
        Guid membershipId,
        Guid weekGameSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        int totalCount = await database.WeekGameSetGames
            .AsNoTracking()
            .CountAsync(g => g.WeekGameSetId == weekGameSetId && !g.IsRemoved && !g.IsVoided, cancellationToken);

        int pickedCount = await database.Picks
            .AsNoTracking()
            .CountAsync(
                p => p.MembershipId == membershipId
                    && p.WeekGameSetGame!.WeekGameSetId == weekGameSetId
                    && !p.WeekGameSetGame.IsRemoved
                    && !p.WeekGameSetGame.IsVoided,
                cancellationToken);

        return Math.Max(0, totalCount - pickedCount);
    }
}
