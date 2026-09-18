namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// A member's role inside one league. Stored as <c>tinyint</c> on <c>Memberships.Role</c>.
/// </summary>
/// <remarks>
/// A league may have many commissioners (D-002); authorization always checks this value and
/// never a single owner column on <c>Leagues</c>.
/// </remarks>
public enum MembershipRole : byte
{
    /// <summary>Makes picks, reads the league. The default for everyone who accepts an invite.</summary>
    Member = 0,

    /// <summary>Member plus league configuration, point values, corrections, and member management.</summary>
    Commissioner = 1,
}
