namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Which external data provider a row came from. Used by <c>TeamAliases.Source</c> and
/// <c>UnmatchedGames.Source</c>.
/// </summary>
public enum ProviderSource : byte
{
    /// <summary>CollegeFootballData, the reference-data source of truth (D-003).</summary>
    Cfbd = 0,

    /// <summary>ESPN's public scoreboard, the live-score source (D-003).</summary>
    Espn = 1,
}
