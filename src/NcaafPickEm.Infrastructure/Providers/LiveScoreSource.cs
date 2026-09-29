namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Which live-score implementation is in play. The configured value comes from
/// <c>Providers:LiveScores</c>; the active value can differ from it while the ESPN-to-CFBD
/// fallback is engaged (04-Domain-Algorithms.md section 10).
/// </summary>
/// <remarks>
/// Deliberately not <c>Shared.Enums.ProviderSource</c>: that enum names the two real data
/// providers rows are attributed to, and <see cref="Fixture"/> is neither.
/// </remarks>
public enum LiveScoreSource
{
    /// <summary>The offline sample week. Never falls back, never reports staleness.</summary>
    Fixture = 0,

    /// <summary>ESPN's public scoreboard: the default, free and richest source.</summary>
    Espn = 1,

    /// <summary>CollegeFootballData's games endpoint: Scheduled or Final only.</summary>
    Cfbd = 2,

    /// <summary>
    /// <c>tools/NcaafPickEm.Simulator</c> applies scores itself; the app polls nothing
    /// (P11-01, D-183). Development and Testing only.
    /// </summary>
    Simulator = 3,
}
