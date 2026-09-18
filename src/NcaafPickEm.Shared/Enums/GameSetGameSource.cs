namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// How a game got into a week's game set.
/// </summary>
public enum GameSetGameSource : byte
{
    /// <summary>Selected by a <c>GameSetRules</c> row during generation.</summary>
    Rule = 0,

    /// <summary>Added by a commissioner by hand; survives regeneration.</summary>
    Manual = 1,
}
