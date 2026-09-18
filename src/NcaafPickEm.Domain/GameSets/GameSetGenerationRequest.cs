namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// Everything <see cref="GameSetGenerator"/> needs for one week of one league. Preview reuses the
/// same request with candidate rules swapped in: <c>request with { Rules = candidates }</c>.
/// </summary>
public sealed record GameSetGenerationRequest
{
    /// <summary>The provider week being generated. Only used to pick the poll to rank against.</summary>
    public required int Week { get; init; }

    /// <summary>Every game the provider has for this season and week, eligible or not.</summary>
    public required IReadOnlyList<GameInfo> Games { get; init; }

    /// <summary>
    /// The rules in force for this week: the week override when the set uses one, otherwise the
    /// league default. An empty list selects nothing, which is a valid (empty) set.
    /// </summary>
    public required IReadOnlyList<RuleInfo> Rules { get; init; }

    /// <summary>
    /// Every AP poll the caller holds for the season. The generator picks per
    /// <see cref="RankingSet"/>; passing only the current and prior week's polls is enough.
    /// </summary>
    public IReadOnlyList<RankingSet> Rankings { get; init; } = [];

    /// <summary>The week's current <c>WeekGameSetGames</c> rows, removed ones included.</summary>
    public IReadOnlyList<ExistingSetGame> ExistingGames { get; init; } = [];

    /// <summary>
    /// True once the week's lock job has run (<c>WeekGameSets.LockedUtc</c> is set). Generation is
    /// a no-op from then on; preview ignores it.
    /// </summary>
    public bool IsLocked { get; init; }
}
