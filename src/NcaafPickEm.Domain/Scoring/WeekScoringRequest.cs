namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// Everything <see cref="WeekScorer"/> needs to score one locked week of one league. Flattened
/// records rather than entities, so the rules are testable without a database.
/// </summary>
/// <remarks>
/// There is no clock here and no week number: a week is scored from its rows alone, so the same
/// inputs always produce the same output and a recompute is idempotent by construction (D-006).
/// </remarks>
public sealed record WeekScoringRequest
{
    /// <summary>
    /// Every row of the week's set, removed and voided ones included; <see cref="WeekScorer"/>
    /// filters.
    /// </summary>
    public required IReadOnlyList<ScoringGame> Games { get; init; }

    /// <summary>
    /// Every membership with a <c>WeekSubmissions</c> row for the set, whatever its status;
    /// <see cref="WeekScorer"/> keeps the ones the lock job settled.
    /// </summary>
    public required IReadOnlyList<ScoringMember> Members { get; init; }

    /// <summary>
    /// Every pick on the set's games. Picks from an unlisted membership, or on an unlisted game,
    /// are ignored.
    /// </summary>
    public IReadOnlyList<ScoringPick> Picks { get; init; } = [];
}
