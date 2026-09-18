namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// What one generation or preview produced. Nothing here is persisted: the application service
/// decides what to write, what to raise, and what to refuse.
/// </summary>
/// <param name="Games">
/// The week's games after rules, manual adds, and sticky removals, ordered by kickoff then game
/// id so two runs over the same inputs produce the same list.
/// </param>
/// <param name="Added">
/// Game ids in <paramref name="Games"/> that the set does not already hold as an active row, in
/// the same order. The service inserts these and raises <c>GameAddedToSet</c>.
/// </param>
/// <param name="Removed">
/// Active rule-sourced rows the rules no longer select, ordered by game id. The service flags
/// them <c>IsRemoved</c> with reason "Rule regeneration" rather than deleting them, so picks
/// survive, and raises <c>GameRemovedFromSet</c>. Manual rows are never in this list: a manual
/// add outlives any rule change.
/// </param>
/// <param name="RemovedIneligible">
/// Active rows, of either source, whose game is no longer eligible at all - postponed, cancelled,
/// no longer a Saturday Eastern kickoff, or no longer FBS on both sides - ordered by game id and
/// disjoint from <paramref name="Removed"/>. The story requires these out of the set whatever put
/// them there, so a manual add can appear here; the reason is a schedule change, not the rules.
/// </param>
/// <param name="ExceedsMax">
/// True when <paramref name="Games"/> holds more than <see cref="GameSetLimits.MaxGames"/>. The
/// list is still returned in full so a preview can show what the rules would produce; the service
/// refuses to save (409).
/// </param>
/// <param name="UsedFallbackRankings">
/// True when a Top 25 rule was applied and no poll existed for the week, so the most recent prior
/// week's poll was used. False when no Top 25 rule was in play, or when no poll existed at all.
/// </param>
/// <param name="LockAtUtc">
/// Earliest kickoff among <paramref name="Games"/>, or null when the set is empty.
/// </param>
/// <param name="IsLocked">
/// True when generation was refused because the week is already locked. Everything else is empty
/// or false: generation after lock is a no-op here, and turning that into a 409 is the service's
/// job. Preview never sets this.
/// </param>
public sealed record GenerationResult(
    IReadOnlyList<GeneratedGame> Games,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed,
    IReadOnlyList<Guid> RemovedIneligible,
    bool ExceedsMax,
    bool UsedFallbackRankings,
    DateTime? LockAtUtc,
    bool IsLocked)
{
    /// <summary>
    /// The refusal returned for a locked week: no games, no diff, nothing for a caller to act on
    /// except <see cref="IsLocked"/>.
    /// </summary>
    public static GenerationResult Locked { get; } = new(
        Games: [],
        Added: [],
        Removed: [],
        RemovedIneligible: [],
        ExceedsMax: false,
        UsedFallbackRankings: false,
        LockAtUtc: null,
        IsLocked: true);

    /// <summary>How many games the set would hold. What <c>ExceedsMax</c> is measured against.</summary>
    public int Count => Games.Count;
}
