namespace NcaafPickEm.Shared.Contracts.Reference;

/// <summary>One FBS conference from <c>GET /api/reference/conferences</c>.</summary>
/// <param name="ConferenceId">Our <c>Conferences.Id</c>.</param>
/// <param name="Name">Full name, e.g. "Big Ten Conference".</param>
/// <param name="Abbreviation">Short form, e.g. "B1G".</param>
public sealed record ConferenceDto(
    Guid ConferenceId,
    string Name,
    string Abbreviation);
