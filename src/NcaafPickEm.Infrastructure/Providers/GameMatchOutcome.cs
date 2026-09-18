namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>What the matcher concluded about one provider update.</summary>
public enum GameMatchOutcome
{
    /// <summary>Matched on a stored provider id: the fast path after the first match.</summary>
    MatchedById = 0,

    /// <summary>Matched on the normalized team pair, which is what fills the provider id in.</summary>
    MatchedByTeams = 1,

    /// <summary>
    /// Both teams look like FBS schools but no game in the window fits. Surfaced on the data
    /// status page as an <c>UnmatchedGames</c> row so a commissioner can resolve it.
    /// </summary>
    Unmatched = 2,

    /// <summary>
    /// Not ours to worry about: an FCS (or lower) participant, or two names we have never heard
    /// of. <c>groups=80</c> is not an FBS filter, so this is most of a Saturday payload (D-012).
    /// </summary>
    Ignored = 3,

    /// <summary>More than one game in the window fits. Never guessed; treated as unmatched.</summary>
    Ambiguous = 4,
}
