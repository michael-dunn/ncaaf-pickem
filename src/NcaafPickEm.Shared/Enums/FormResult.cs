namespace NcaafPickEm.Shared.Enums;

/// <summary>A recent game's result from one team's point of view (Feature 17, 04-Domain-Algorithms section 14).</summary>
public enum FormResult : byte
{
    /// <summary>The team scored more.</summary>
    Win = 0,

    /// <summary>The team scored less.</summary>
    Loss = 1,

    /// <summary>The scores were equal.</summary>
    Tie = 2,
}
