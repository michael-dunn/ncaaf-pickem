namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// What scoring one week produced. Nothing here is persisted: the application service upserts the
/// <c>WeekResults</c> rows, stamps <c>WeekGameSets.IsComplete</c>, and decides whether a season
/// standings snapshot is due.
/// </summary>
/// <param name="Members">
/// One score per membership the lock job settled, in the order the request listed them. A
/// membership that is absent must not be given a <c>WeekResults</c> row.
/// </param>
/// <param name="IsWeekComplete">
/// True once every active game has a determinable winner. A voided game does not hold the week
/// open; a Final tie or a Final game missing a score does, until a commissioner overrides or
/// voids it (<c>04-Domain-Algorithms.md</c> section 7).
/// </param>
/// <param name="NeedsReviewGameSetGameIds">
/// The active rows that are Final but have no determinable winner, in the order the request
/// listed them. These award nothing to anybody and are what the data status page lists under
/// "needs review".
/// </param>
public sealed record WeekScoreResult(
    IReadOnlyList<MemberWeekScore> Members,
    bool IsWeekComplete,
    IReadOnlyList<Guid> NeedsReviewGameSetGameIds);
