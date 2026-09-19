using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Scoring.Events;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Rescores the one league week a commissioner voided a game in (Feature 06, "it is removed from
/// scoring for that week and awards 0 to everyone").
/// </summary>
/// <remarks>
/// A void changes more than one number: the game stops earning anybody points and also leaves
/// <c>ActiveGameCount</c>, so "4 of 9" becomes "4 of 8" for the whole league. It can also complete
/// the week, when the voided game was the last one without a determinable winner.
/// </remarks>
public sealed class GameVoidedScoringHandler : IDomainEventHandler<GameVoided>
{
    private readonly ScoringService _scoring;
    private readonly ILogger<GameVoidedScoringHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="scoring">The scorer.</param>
    /// <param name="logger">Structured log sink.</param>
    public GameVoidedScoringHandler(ScoringService scoring, ILogger<GameVoidedScoringHandler> logger)
    {
        _scoring = scoring;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameVoided domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "Membership {ActorMembershipId} voided game {GameId} in league {LeagueId} week {Week} "
            + "({Reason}); rescoring the week",
            domainEvent.ActorMembershipId,
            domainEvent.GameId,
            domainEvent.LeagueId,
            domainEvent.Week,
            domainEvent.Reason);

        await _scoring.RescoreWeekAsync(domainEvent.WeekGameSetId, cancellationToken).ConfigureAwait(false);
    }
}
