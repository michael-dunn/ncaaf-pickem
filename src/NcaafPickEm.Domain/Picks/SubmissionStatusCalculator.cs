using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// Derives a member's week status from their picks (04-Domain-Algorithms.md section 4). Pure: the
/// caller supplies the active games, the picks, and when Submit was last pressed.
/// </summary>
/// <remarks>
/// Only ever applied while the week is unlocked. <see cref="SubmissionStatus.Locked"/> and
/// <see cref="SubmissionStatus.Incomplete"/> are written once by the lock job (section 5) and must
/// survive every later recompute, so the caller skips this calculator when
/// <c>WeekGameSets.LockedUtc</c> is set.
/// </remarks>
public static class SubmissionStatusCalculator
{
    /// <summary>
    /// Works out <see cref="SubmissionState"/> for one member.
    /// </summary>
    /// <param name="activeGames">
    /// Every active game in the week's set (neither removed nor voided), with the instant each
    /// joined the set. Order does not matter; duplicate ids are counted once.
    /// </param>
    /// <param name="pickedGameSetGameIds">
    /// Every game the member holds a pick on. Picks on games that are no longer active are kept in
    /// the database and simply ignored here, so a removed game never drags a member out of
    /// <see cref="SubmissionStatus.Submitted"/>.
    /// </param>
    /// <param name="submittedUtc">
    /// When the member last pressed Submit for this week, or null if they never have.
    /// </param>
    /// <returns>The status plus the picked and total counts the UI shows as "n of m".</returns>
    public static SubmissionState Calculate(
        IReadOnlyCollection<ActiveSetGame> activeGames,
        IReadOnlyCollection<Guid> pickedGameSetGameIds,
        DateTime? submittedUtc)
    {
        ArgumentNullException.ThrowIfNull(activeGames);
        ArgumentNullException.ThrowIfNull(pickedGameSetGameIds);

        HashSet<Guid> picked = [.. pickedGameSetGameIds];
        HashSet<Guid> active = [.. activeGames.Select(game => game.GameSetGameId)];

        int totalCount = active.Count;
        int pickedCount = active.Count(picked.Contains);

        if (pickedCount == 0)
        {
            return new SubmissionState(SubmissionStatus.NotStarted, pickedCount, totalCount);
        }

        if (pickedCount < totalCount)
        {
            return new SubmissionState(SubmissionStatus.InProgress, pickedCount, totalCount);
        }

        // Every active game is picked. Submitted only holds if Submit was pressed since the last
        // time the set grew: a game added after Submit reverts the member to In Progress until
        // they submit again (Feature 04, "new game highlighted").
        DateTime lastAddedUtc = activeGames.Max(game => game.AddedUtc);
        SubmissionStatus status = submittedUtc is DateTime submitted && submitted >= lastAddedUtc
            ? SubmissionStatus.Submitted
            : SubmissionStatus.InProgress;

        return new SubmissionState(status, pickedCount, totalCount);
    }
}
