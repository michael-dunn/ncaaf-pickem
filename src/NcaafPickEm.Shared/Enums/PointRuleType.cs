namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Kind of rule that assigns a point value to a game in a set (Feature 03).
/// Rules are ordered by priority; the first match wins.
/// </summary>
public enum PointRuleType : byte
{
    /// <summary>Matches when the game is a conference game.</summary>
    ConferenceGame = 0,

    /// <summary>Matches when the absolute spread is strictly below <c>SpreadThreshold</c>. A game
    /// with no spread never matches.</summary>
    CloseSpread = 1,

    /// <summary>Matches when the named team is playing.</summary>
    Team = 2,
}
