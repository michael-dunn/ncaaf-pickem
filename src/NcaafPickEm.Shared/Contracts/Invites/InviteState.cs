namespace NcaafPickEm.Shared.Contracts.Invites;

/// <summary>Whether an invite code can be accepted by the caller right now.</summary>
public enum InviteState
{
    /// <summary>Accept will succeed.</summary>
    Valid = 0,

    /// <summary>Past <c>ExpiresUtc</c>.</summary>
    Expired = 1,

    /// <summary>Revoked by a commissioner.</summary>
    Revoked = 2,

    /// <summary>The league is at the member cap or the invite has no uses left.</summary>
    Full = 3,

    /// <summary>The caller already has an active membership in the league.</summary>
    AlreadyMember = 4,
}
