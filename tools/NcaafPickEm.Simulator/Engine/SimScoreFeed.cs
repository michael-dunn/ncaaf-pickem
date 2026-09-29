using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// The simulator's scoreboard: its <see cref="ILiveScoreProvider"/>. A step queues the updates it
/// wants applied and then runs <c>SaturdayPoller.PollOnceAsync</c> - the poller's own path - which
/// asks this feed for the Eastern date's scores, hands them to <c>LiveScoreApplyService</c>, and
/// records the refresh on the data page, exactly as a poll of ESPN would.
/// </summary>
/// <remarks>
/// Scoped, and registered before <c>AddInfrastructure</c> so the Simulator branch's
/// <c>TryAddSingleton</c> of the app's always-empty provider no-ops. Updates carry
/// <see cref="Shared.Enums.ProviderSource.Cfbd"/> with the CFBD game id as the event id, the
/// matcher's id path (04-Domain-Algorithms section 9), so no team-name matching is involved.
/// </remarks>
public sealed class SimScoreFeed : ILiveScoreProvider
{
    private readonly List<LiveScoreUpdate> _pending = [];

    /// <summary>Queues updates for the next poll.</summary>
    /// <param name="updates">The scoreboard lines to serve.</param>
    public void Queue(IEnumerable<LiveScoreUpdate> updates) => _pending.AddRange(updates);

    /// <summary>The distinct Eastern kickoff dates of everything queued: one poll per date.</summary>
    public IReadOnlyList<DateOnly> PendingDates() =>
        [.. _pending.Select(update => EasternDate(update.KickoffUtc)).Distinct().Order()];

    /// <inheritdoc />
    public Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LiveScoreUpdate> due = [.. _pending.Where(update => EasternDate(update.KickoffUtc) == easternDate)];
        _pending.RemoveAll(update => EasternDate(update.KickoffUtc) == easternDate);
        return Task.FromResult(due);
    }

    private static DateOnly EasternDate(DateTime kickoffUtc) =>
        DateOnly.FromDateTime(SeasonCalendar.ToEastern(new DateTimeOffset(DateTime.SpecifyKind(kickoffUtc, DateTimeKind.Utc))).DateTime);
}
