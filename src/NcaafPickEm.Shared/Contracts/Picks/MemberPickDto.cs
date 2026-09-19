namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>One member's pick on one game, as the post-lock all-picks view shows it.</summary>
/// <param name="GameSetGameId">The game in the week's set.</param>
/// <param name="TeamId">The team they picked, or null when they never picked it.</param>
public sealed record MemberPickDto(Guid GameSetGameId, Guid? TeamId);
