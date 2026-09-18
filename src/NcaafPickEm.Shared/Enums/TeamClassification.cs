namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// NCAA division of a team or conference, as reported by CollegeFootballData.
/// Only <see cref="Fbs"/> is eligible for game sets (Feature 02).
/// </summary>
public enum TeamClassification : byte
{
    /// <summary>Anything CFBD reports that is neither FBS nor FCS (D2, D3, NAIA, ...).</summary>
    Other = 0,

    /// <summary>Football Bowl Subdivision.</summary>
    Fbs = 1,

    /// <summary>Football Championship Subdivision.</summary>
    Fcs = 2,
}
