using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Scores every locked league week that carries a game the moment it goes Final (Feature 06,
/// "see my points update as games go final").
/// </summary>
/// <remarks>
/// The event's <c>Week</c> is the provider's, not any league's, and this handler never reads it:
/// it asks which <c>WeekGameSets</c> actually hold the game. That is what makes a Saturday game
/// finishing after midnight Eastern score against the week it kicked off in rather than the one
/// the clock has rolled into (Feature 06, "delayed games").
/// <para>
/// A null <see cref="GameWentFinal.WinnerTeamId"/> - the feed reported a tie or no scores - is
/// still worth a rescore: the game becomes a needs-review row, which is what holds the week open
/// instead of quietly completing it.
/// </para>
/// </remarks>
public sealed class GameWentFinalScoringHandler : IDomainEventHandler<GameWentFinal>
{
    private readonly ScoringService _scoring;
    private readonly ILogger<GameWentFinalScoringHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="scoring">The scorer.</param>
    /// <param name="logger">Structured log sink.</param>
    public GameWentFinalScoringHandler(ScoringService scoring, ILogger<GameWentFinalScoringHandler> logger)
    {
        _scoring = scoring;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameWentFinal domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        int weeks = await _scoring.RescoreGameAsync(domainEvent.GameId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Game {GameId} went final (winner {WinnerTeamId}); rescored {WeekCount} league weeks",
            domainEvent.GameId,
            domainEvent.WinnerTeamId,
            weeks);
    }
}
