using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Subscribes to <see cref="GameNeedsVoidReview"/> (Feature 02/06, P3-04's event) so the
/// dispatcher's "no handler" debug noise goes away and there is one place to hook a commissioner
/// notification later. The event itself asks for no data change: <c>GameSetService.ListNeedsVoidReviewAsync</c>
/// is the mechanism P3-04 already chose for surfacing the game
/// (<c>GET /api/admin/data-status</c>'s <c>NeedsReview</c> list, D-100), and P5-02's void/override
/// endpoints are the only things that ever resolve one.
/// </summary>
public sealed class GameNeedsVoidReviewHandler : IDomainEventHandler<GameNeedsVoidReview>
{
    private readonly ILogger<GameNeedsVoidReviewHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public GameNeedsVoidReviewHandler(ILogger<GameNeedsVoidReviewHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(GameNeedsVoidReview domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _logger.LogInformation(
            "League {LeagueId} week {Week}: game {GameId} needs a void/override decision ({Reason}).",
            domainEvent.LeagueId,
            domainEvent.Week,
            domainEvent.GameId,
            domainEvent.Reason);

        return Task.CompletedTask;
    }
}
