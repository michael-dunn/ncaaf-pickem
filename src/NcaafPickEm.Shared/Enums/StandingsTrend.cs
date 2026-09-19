namespace NcaafPickEm.Shared.Enums;

/// <summary>Season rank movement between the two most recent completed weeks (Feature 07).</summary>
public enum StandingsTrend : byte
{
    /// <summary>No previous snapshot to compare against (first complete week, or member joined since).</summary>
    None = 0,

    /// <summary>Rank number went down (better).</summary>
    Up = 1,

    /// <summary>Rank number went up (worse).</summary>
    Down = 2,

    /// <summary>Same rank as the previous complete week.</summary>
    Same = 3,
}
