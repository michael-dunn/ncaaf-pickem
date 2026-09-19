namespace NcaafPickEm.Shared.Contracts.Scoring;

/// <summary>One row of <c>GET /api/leagues/{leagueId}/audit</c>, newest first; visible to every member.</summary>
/// <param name="CreatedUtc">When the action happened.</param>
/// <param name="ActorName">Effective display name of the acting member, or "System".</param>
/// <param name="Action">The <c>AuditAction</c> name, e.g. ResultOverride.</param>
/// <param name="Summary">Human-readable one-liner built from the stored details.</param>
public sealed record AuditEntry(
    DateTimeOffset CreatedUtc,
    string ActorName,
    string Action,
    string Summary);
