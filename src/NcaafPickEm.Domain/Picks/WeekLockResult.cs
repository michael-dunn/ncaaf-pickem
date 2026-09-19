namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// What locking one week produced. Nothing here is persisted: the lock job decides what to write,
/// stamps <c>LockedUtc</c>, and raises <c>WeekLocked</c>.
/// </summary>
/// <param name="Games">
/// One snapshot per active row, in the order the request listed them. Inactive rows are absent
/// and keep whatever they already held.
/// </param>
/// <param name="Members">
/// One verdict per membership that was active at lock and had joined by then, in the order the
/// request listed them. A membership that is absent must not be given a row.
/// </param>
public sealed record WeekLockResult(
    IReadOnlyList<LockedGameSnapshot> Games,
    IReadOnlyList<LockedMemberStatus> Members);
