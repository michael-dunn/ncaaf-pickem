using NcaafPickEm.Domain.Points;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// Everything <see cref="WeekLocker"/> needs to lock one week of one league. Flattened records
/// rather than entities, so the rules are testable without a database.
/// </summary>
public sealed record WeekLockRequest
{
    /// <summary>
    /// Every row of the week's set, inactive ones included; <see cref="WeekLocker"/> filters.
    /// </summary>
    public required IReadOnlyList<WeekLockGame> Games { get; init; }

    /// <summary>
    /// Every membership of the league, removed ones included; <see cref="WeekLocker"/> filters.
    /// </summary>
    public required IReadOnlyList<WeekLockMember> Members { get; init; }

    /// <summary>The league's point rules. <see cref="PointValueResolver"/> sorts them itself.</summary>
    public IReadOnlyList<PointRuleInfo> PointRules { get; init; } = [];

    /// <summary>The league's default point value, used when no override and no rule applies.</summary>
    public required int LeagueDefaultPointValue { get; init; }

    /// <summary>
    /// The instant picks froze — the week's <c>LockAtUtc</c>, not when the job got to it. A
    /// member who joined after this never had a pickable moment in the week and gets no row, so a
    /// job that runs late does not hand rows to people who joined in the meantime.
    /// </summary>
    public required DateTime LockInstantUtc { get; init; }
}
