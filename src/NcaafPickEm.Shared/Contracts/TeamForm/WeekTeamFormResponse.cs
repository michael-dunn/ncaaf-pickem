namespace NcaafPickEm.Shared.Contracts.TeamForm;

/// <summary>
/// Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/team-form</c> (Feature 17): every team in
/// the week's set with its recent form, keyed by team rather than game because a team plays once a
/// week. Empty while the week's picks have not opened, as the picks page is.
/// </summary>
/// <param name="Week">Week number.</param>
/// <param name="Teams">Every team in the set's games that have not been removed.</param>
public sealed record WeekTeamFormResponse(
    int Week,
    TeamFormDto[] Teams);
