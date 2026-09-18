namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// A refreshable slice of provider data. Primary key of <c>DataRefreshStatus</c> and the
/// route value of <c>POST /api/admin/refresh/{dataType}</c>.
/// </summary>
public enum RefreshDataType : byte
{
    /// <summary>Teams and conferences from CFBD.</summary>
    Teams = 0,

    /// <summary>The season schedule from CFBD.</summary>
    Schedule = 1,

    /// <summary>The AP Top 25 from CFBD.</summary>
    Rankings = 2,

    /// <summary>Betting lines from CFBD.</summary>
    Lines = 3,

    /// <summary>Live scores from ESPN (or CFBD as fallback).</summary>
    Scores = 4,
}
