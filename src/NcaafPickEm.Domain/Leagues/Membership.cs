using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// One user's participation in one league (Feature 01). Removal is a soft delete so former
/// members keep their picks and stay on historical grids and leaderboards.
/// </summary>
public sealed class Membership
{
    /// <summary>Maximum length of <see cref="DisplayNameOverride"/>, in characters.</summary>
    public const int DisplayNameMaxLength = 30;

    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public MembershipRole Role { get; set; }

    /// <summary>Per-league name. When null the league sees <c>Users.DisplayName</c>.</summary>
    public string? DisplayNameOverride { get; set; }

    public DateTime JoinedUtc { get; set; }

    /// <summary>The week that was current at join. There is no participation before it.</summary>
    public int JoinedWeek { get; set; }

    /// <summary>Non-null once removed. An "active" membership is one where this is null.</summary>
    public DateTime? RemovedUtc { get; set; }

    /// <summary>True while the member still counts for picks, scoring, and authorization.</summary>
    public bool IsActive => RemovedUtc is null;
}
