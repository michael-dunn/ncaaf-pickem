namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// One member's choice of winner for one row of the week's set, as <see cref="WeekScorer"/> sees
/// it.
/// </summary>
/// <remarks>
/// Picks belonging to a membership the caller did not list, or to a game-set game the caller did
/// not list, are ignored outright - the same rule the influence dashboard uses (D-095). Picks on
/// removed or voided games are kept in the table (D-008) and simply never score.
/// </remarks>
/// <param name="MembershipId">Whose pick it is.</param>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> the pick is on.</param>
/// <param name="TeamId">The team picked; always the home or away team of that game.</param>
public readonly record struct ScoringPick(Guid MembershipId, Guid GameSetGameId, Guid TeamId);
