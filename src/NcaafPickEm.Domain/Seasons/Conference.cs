using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// An NCAA conference, ingested from CollegeFootballData (Feature 09).
/// </summary>
public sealed class Conference
{
    /// <summary>Maximum length of <see cref="Name"/>, in characters.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Maximum length of <see cref="Abbreviation"/>, in characters.</summary>
    public const int AbbreviationMaxLength = 20;

    public Guid Id { get; set; }

    /// <summary>CollegeFootballData's conference id. The natural key for upserts.</summary>
    public int CfbdId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short form, e.g. "SEC". Required but allowed to be empty: CFBD leaves it blank for most
    /// non-FBS conferences (46 of the 72 it reports for 2026), so the read models fall back to
    /// <see cref="Name"/> rather than the column becoming nullable (D-171).
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    public TeamClassification Classification { get; set; }

    /// <summary>ESPN's group id, filled by matching so score lookups can be scoped.</summary>
    public int? EspnGroupId { get; set; }
}
