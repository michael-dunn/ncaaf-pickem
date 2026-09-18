namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Where a game's resolved point value came from (Feature 03). Not persisted; it travels with a
/// resolution so the UI can explain a value the commissioner did not expect.
/// </summary>
public enum PointValueSource : byte
{
    /// <summary>No rule matched, so the league default applies.</summary>
    Default = 0,

    /// <summary>The highest-priority matching point rule decided the value.</summary>
    Rule = 1,

    /// <summary>The commissioner set a manual value on this game for this week.</summary>
    Override = 2,
}
