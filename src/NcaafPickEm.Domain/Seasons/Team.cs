using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// A college football team, ingested from CollegeFootballData (Feature 09).
/// Only <see cref="TeamClassification.Fbs"/> teams are eligible for game sets (Feature 02).
/// </summary>
public sealed class Team
{
    public Guid Id { get; set; }

    /// <summary>CollegeFootballData's team id. The natural key for upserts.</summary>
    public int CfbdId { get; set; }

    public string School { get; set; } = string.Empty;

    public string? Mascot { get; set; }

    public string? Abbreviation { get; set; }

    public Guid? ConferenceId { get; set; }

    public Conference? Conference { get; set; }

    public TeamClassification Classification { get; set; }

    /// <summary>Provider URL. Logos are never copied locally.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>ESPN's team id, filled by the matcher in P2-03.</summary>
    public int? EspnTeamId { get; set; }
}
