using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Scoring.Events;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Rescores the one league week a commissioner corrected (Feature 06, "when they override a
/// game's winner, all members' points for that game are recalculated").
/// </summary>
/// <remarks>
/// The event names its own <c>WeekGameSets</c> row, so this rescores that week alone - another
/// league carrying the same underlying game keeps the scoreboard's answer. Correcting a game that
/// was the last thing holding the week open is what finally completes it and triggers the season
/// standings snapshot; that decision lives in <see cref="ScoringService"/>, not here.
/// </remarks>
public sealed class ResultOverriddenScoringHandler : IDomainEventHandler<ResultOverridden>
{
    private readonly ScoringService _scoring;
    private readonly ILogger<ResultOverriddenScoringHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="scoring">The scorer.</param>
    /// <param name="logger">Structured log sink.</param>
    public ResultOverriddenScoringHandler(ScoringService scoring, ILogger<ResultOverriddenScoringHandler> logger)
    {
        _scoring = scoring;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ResultOverridden domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "Membership {ActorMembershipId} overrode game {GameId} in league {LeagueId} week {Week} "
            + "to winner {WinnerTeamId}; rescoring the week",
            domainEvent.ActorMembershipId,
            domainEvent.GameId,
            domainEvent.LeagueId,
            domainEvent.Week,
            domainEvent.WinnerTeamId);

        await _scoring.RescoreWeekAsync(domainEvent.WeekGameSetId, cancellationToken).ConfigureAwait(false);
    }
}
