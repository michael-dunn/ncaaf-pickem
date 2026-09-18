using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// An NCAA conference, ingested from CollegeFootballData (Feature 09).
/// </summary>
public sealed class Conference
{
    public Guid Id { get; set; }

    /// <summary>CollegeFootballData's conference id. The natural key for upserts.</summary>
    public int CfbdId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    public TeamClassification Classification { get; set; }

    /// <summary>ESPN's group id, filled by matching so score lookups can be scoped.</summary>
    public int? EspnGroupId { get; set; }
}
