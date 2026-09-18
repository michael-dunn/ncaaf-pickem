namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Kind of rule that selects games into a week's game set (Feature 02). Rules are unioned.
/// </summary>
public enum GameSetRuleType : byte
{
    /// <summary>Every Saturday FBS game with at least one AP Top 25 team.</summary>
    Top25 = 0,

    /// <summary>Every Saturday game involving a team in the named conference.</summary>
    Conference = 1,

    /// <summary>Every Saturday game involving the named team.</summary>
    Team = 2,
}
