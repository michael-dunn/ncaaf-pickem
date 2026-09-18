using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// One commissioner action, visible to every member of the league (Feature 06).
/// Maps to the <c>AuditLog</c> table.
/// </summary>
public sealed class AuditLogEntry
{
    /// <summary>Maximum length of <see cref="Details"/> is unbounded; it holds JSON.</summary>
    public const int ActionMaxLength = 40;

    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    public Guid ActorMembershipId { get; set; }

    public Membership? ActorMembership { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>The row the action was about: a game-set game, a membership, and so on.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>JSON blob with whatever the action needs to be rendered as a sentence.</summary>
    public string Details { get; set; } = "{}";

    public DateTime CreatedUtc { get; set; }
}
