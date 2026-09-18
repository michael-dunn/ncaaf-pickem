namespace NcaafPickEm.Shared.Contracts.Reference;

/// <summary>A team as shown in game cards and pickers (<c>GET /api/reference/teams</c> and embedded in game DTOs).</summary>
/// <param name="TeamId">Our <c>Teams.Id</c>.</param>
/// <param name="School">School name, e.g. "Michigan".</param>
/// <param name="Abbreviation">Short code, e.g. "MICH"; may be null for some FCS teams.</param>
/// <param name="ConferenceId">Conference, when known.</param>
/// <param name="LogoUrl">Provider logo URL, when known.</param>
public sealed record TeamDto(
    Guid TeamId,
    string School,
    string? Abbreviation,
    Guid? ConferenceId,
    string? LogoUrl);
