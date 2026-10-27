namespace NcaafPickEm.Shared.Contracts.TeamForm;

/// <summary>One team in the week's set and its last games before its game this week.</summary>
/// <param name="TeamId">The team.</param>
/// <param name="Games">Up to three recent games, newest first; empty when the team has none yet.</param>
public sealed record TeamFormDto(
    Guid TeamId,
    RecentGameDto[] Games);
