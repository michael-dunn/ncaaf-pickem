using NcaafPickEm.Domain.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// Freezes one week (<c>04-Domain-Algorithms.md</c> section 5, steps 1 and 2). Pure: no clock, no
/// I/O, no entities. <c>Infrastructure/Jobs/LockWeekJob</c> loads the rows, applies what comes
/// back, stamps <c>LockedUtc</c>, and raises <c>WeekLocked</c>.
/// </summary>
/// <remarks>
/// Statuses come from <see cref="SubmissionStatusCalculator"/> rather than from a second copy of
/// section 4's rules, so "Submitted" means exactly the same thing a second before lock as it does
/// at lock — a game added after a member pressed Submit leaves them In Progress, and therefore
/// Incomplete.
/// </remarks>
public static class WeekLocker
{
    /// <summary>
    /// Works out the frozen snapshot for every active game and the final status for every
    /// membership that gets a row.
    /// </summary>
    /// <param name="request">The week's games, memberships, point configuration, and lock instant.</param>
    /// <returns>What the caller should write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static WeekLockResult Lock(WeekLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        WeekLockGame[] active = [.. request.Games.Where(game => game.IsActive)];

        var snapshots = new LockedGameSnapshot[active.Length];
        for (int i = 0; i < active.Length; i++)
        {
            WeekLockGame game = active[i];

            // Section 3: the value is resolved one final time against the spread being frozen
            // alongside it, so a close-spread rule and SpreadAtLock can never disagree.
            PointResolution resolution = PointValueResolver.Resolve(
                game.Game,
                game.PointValueOverride,
                request.LeagueDefaultPointValue,
                request.PointRules,
                game.CurrentSpread);

            snapshots[i] = new LockedGameSnapshot(game.GameSetGameId, game.CurrentSpread, resolution.Value);
        }

        ActiveSetGame[] activeGames = [.. active.Select(game => new ActiveSetGame(game.GameSetGameId, game.AddedUtc))];

        var statuses = new List<LockedMemberStatus>(request.Members.Count);
        foreach (WeekLockMember member in request.Members)
        {
            // Active at lock = still a member, and a member already: someone who joins while the
            // games are being played has no week here to be Incomplete in.
            if (!member.IsActive || member.JoinedUtc > request.LockInstantUtc)
            {
                continue;
            }

            SubmissionState state = SubmissionStatusCalculator.Calculate(
                activeGames,
                member.PickedGameSetGameIds,
                member.SubmittedUtc);

            SubmissionStatus status = state.Status == SubmissionStatus.Submitted
                ? SubmissionStatus.Locked
                : SubmissionStatus.Incomplete;

            statuses.Add(new LockedMemberStatus(member.MembershipId, status));
        }

        return new WeekLockResult(snapshots, statuses);
    }
}
