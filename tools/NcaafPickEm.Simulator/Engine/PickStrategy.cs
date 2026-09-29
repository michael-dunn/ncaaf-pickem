namespace NcaafPickEm.Simulator.Engine;

/// <summary>How "fill picks" chooses a side.</summary>
public enum PickStrategy
{
    /// <summary>The favourite, with every player after the first flipping a different third: a spread-out leaderboard.</summary>
    Mixed = 0,

    /// <summary>Always the favourite (by the line, then by rank, then home).</summary>
    Favorites = 1,

    /// <summary>Always the underdog.</summary>
    Underdogs = 2,

    /// <summary>Always the home team.</summary>
    HomeTeams = 3,

    /// <summary>A coin flip per game, stable per player and game.</summary>
    Random = 4,
}
