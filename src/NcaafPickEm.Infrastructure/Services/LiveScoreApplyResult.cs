using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>What one live-score apply run did, for the poller's log and the data status page.</summary>
/// <param name="Matched">Updates tied to a game.</param>
/// <param name="Changed">Games whose row actually changed.</param>
/// <param name="Unmatched">Updates written to <c>UnmatchedGames</c> (new rows only).</param>
/// <param name="Ignored">Updates deliberately skipped: FCS noise, or games outside our schedule.</param>
/// <param name="Events">
/// The domain events raised, already saved and already dispatched. Returned so callers and tests
/// can see what happened without subscribing.
/// </param>
public sealed record LiveScoreApplyResult(
    int Matched,
    int Changed,
    int Unmatched,
    int Ignored,
    IReadOnlyList<IDomainEvent> Events);
