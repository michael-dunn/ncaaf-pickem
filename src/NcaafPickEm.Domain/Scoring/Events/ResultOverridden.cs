using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.Scoring.Events;

/// <summary>
/// A commissioner corrected one game's winner for one league's locked week (Feature 06,
/// "commissioner corrections"). Raised by P5-02's override endpoint after the
/// <c>WeekGameSetGames.ResultOverrideWinnerTeamId</c> and the <c>AuditLog</c> row are saved;
/// scoring subscribes and rescores the week.
/// </summary>
/// <remarks>
/// League-scoped, unlike <c>GameWentFinal</c>: an override is one league's decision about one row
/// of its own set, and a second league carrying the same underlying game is untouched. That is
/// why the event names the <c>WeekGameSets</c> row directly rather than making the subscriber
/// work out which weeks a game belongs to.
/// </remarks>
/// <param name="LeagueId">The league whose set was corrected.</param>
/// <param name="Week">The league's week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> to rescore.</param>
/// <param name="GameSetGameId">The corrected <c>WeekGameSetGames.Id</c>.</param>
/// <param name="GameId">The underlying <c>Games.Id</c>, for logs and notifications.</param>
/// <param name="WinnerTeamId">The winner the commissioner declared; always home or away.</param>
/// <param name="ActorMembershipId">The commissioner who did it, matching the audit row.</param>
public sealed record ResultOverridden(
    Guid LeagueId,
    int Week,
    Guid WeekGameSetId,
    Guid GameSetGameId,
    Guid GameId,
    Guid WinnerTeamId,
    Guid ActorMembershipId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
