namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// Hard limits on a week's game set (Feature 02).
/// </summary>
public static class GameSetLimits
{
    /// <summary>
    /// The most games one week's set may hold. A configuration that produces more is reported
    /// through <see cref="GenerationResult.ExceedsMax"/>; generation is refused and the
    /// commissioner must narrow the rules or remove games before saving.
    /// </summary>
    public const int MaxGames = 50;
}
