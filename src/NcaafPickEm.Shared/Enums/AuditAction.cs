namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Action recorded in <c>AuditLog</c> and shown to every member (Feature 06).
/// Persisted as its name in an <c>nvarchar(40)</c> column so the table stays readable in SQL.
/// </summary>
public enum AuditAction
{
    /// <summary>A commissioner corrected the winner of a game after lock.</summary>
    ResultOverride = 0,

    /// <summary>A commissioner voided a game so it scores for nobody.</summary>
    GameVoided = 1,

    /// <summary>A commissioner added a game to a week by hand.</summary>
    GameManuallyAdded = 2,

    /// <summary>A commissioner removed a game from a week by hand.</summary>
    GameManuallyRemoved = 3,

    /// <summary>A commissioner removed a member from the league.</summary>
    MemberRemoved = 4,

    /// <summary>A member was promoted to commissioner.</summary>
    RolePromoted = 5,

    /// <summary>A commissioner was demoted to member.</summary>
    RoleDemoted = 6,

    /// <summary>The commissioner role was transferred in one step.</summary>
    RoleTransferred = 7,

    /// <summary>A commissioner forced a provider data refresh.</summary>
    ManualRefresh = 8,
}
