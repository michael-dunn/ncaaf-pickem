namespace NcaafPickEm.Domain.Seasons;

/// <summary>Where "now" sits relative to a season's weeks.</summary>
public enum SeasonState
{
    /// <summary>Before the first week's window opens: the league home shows "Season starts Week N".</summary>
    BeforeSeason = 0,

    /// <summary>Inside a week window.</summary>
    InSeason = 1,

    /// <summary>After the last regular-season week's window closed.</summary>
    SeasonOver = 2,
}
