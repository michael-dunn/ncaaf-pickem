using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>How often <see cref="SaturdayPoller"/> should call the live-score provider.</summary>
public enum PollerCadence
{
    /// <summary>Outside the window: never.</summary>
    None = 0,

    /// <summary>ESPN is the active source.</summary>
    FiveMinutes = 5,

    /// <summary>CFBD is the active source (fallback, or configured directly).</summary>
    TenMinutes = 10,
}

/// <summary>
/// One league's current-week game set, flattened for the pure schedule decision
/// (04-Domain-Algorithms.md section 10). <see cref="LockAtUtc"/> mirrors
/// <c>WeekGameSet.LockAtUtc</c> (null when the set has no active game); <see cref="AllGamesTerminal"/>
/// is true once every game that still counts (not removed) is Final, Voided, Postponed or
/// Cancelled.
/// </summary>
public sealed record PollerWeekSet(Guid LeagueId, DateTime? LockAtUtc, bool AllGamesTerminal);

/// <summary>What the poller should do right now.</summary>
/// <param name="InWindow">True when a live-score call should be made this tick.</param>
/// <param name="NextPollAtUtc">
/// When the next call should happen: <c>nowUtc + cadence</c> while in the window, the window's
/// start time while waiting for it, or null once the window is closed for the day.
/// </param>
/// <param name="Cadence">
/// The polling interval for the currently active source, reported even when
/// <see cref="InWindow"/> is false so a caller always knows what cadence would apply.
/// </param>
public sealed record PollerDecision(bool InWindow, DateTimeOffset? NextPollAtUtc, PollerCadence Cadence);

/// <summary>
/// The pure decision logic behind <see cref="SaturdayPoller"/> (04-Domain-Algorithms.md
/// section 10): when the window is open, and how often to call the live-score provider while it
/// is. Takes no clock and no database of its own, so it is tested at arbitrary instants.
/// </summary>
public static class SaturdayPollerSchedule
{
    /// <summary>Minutes the window opens ahead of the earliest still-playing set's lock time.</summary>
    public const int WindowOpensBeforeLockMinutes = 5;

    /// <summary>Eastern hour the window closes at, on the Sunday following the Saturday it covers.</summary>
    public const int HardCloseHourEastern = 3;

    /// <summary>
    /// Decides whether to poll right now.
    /// </summary>
    /// <param name="nowUtc">The instant to evaluate.</param>
    /// <param name="activeSets">
    /// Every league's current-week set that has ever had an active game (a set with no active
    /// game at all reports <see cref="PollerWeekSet.LockAtUtc"/> null and is ignored, same as one
    /// whose games have all gone terminal).
    /// </param>
    /// <param name="activeSource">
    /// <see cref="ILiveScoreHealth.ActiveSource"/> right now, which decides the cadence and can
    /// change mid-Saturday once the ESPN fallback engages.
    /// </param>
    public static PollerDecision Evaluate(
        DateTimeOffset nowUtc,
        IReadOnlyList<PollerWeekSet> activeSets,
        LiveScoreSource activeSource)
    {
        ArgumentNullException.ThrowIfNull(activeSets);

        PollerCadence cadence = activeSource == LiveScoreSource.Cfbd
            ? PollerCadence.TenMinutes
            : PollerCadence.FiveMinutes;

        List<PollerWeekSet> withLock = [.. activeSets.Where(set => set.LockAtUtc is not null)];
        if (withLock.Count == 0)
        {
            return new PollerDecision(false, null, cadence);
        }

        List<PollerWeekSet> stillPlaying = [.. withLock.Where(set => !set.AllGamesTerminal)];
        if (stillPlaying.Count == 0)
        {
            // Every set that ever had an active game has finished. The window is closed for the
            // day whether or not it had formally opened yet.
            return new PollerDecision(false, null, cadence);
        }

        DateTimeOffset earliestLockUtc = new(
            DateTime.SpecifyKind(stillPlaying.Min(set => set.LockAtUtc!.Value), DateTimeKind.Utc));
        DateTimeOffset windowStart = earliestLockUtc.AddMinutes(-WindowOpensBeforeLockMinutes);

        // Every game in a set is a Saturday-Eastern game (the pool GameSetGenerator draws from),
        // so the earliest lock's Eastern date is always the Saturday this window covers.
        DateOnly saturdayEastern = DateOnly.FromDateTime(SeasonCalendar.ToEastern(earliestLockUtc).Date);
        DateTimeOffset hardClose = SeasonCalendar.ToUtc(
            saturdayEastern.AddDays(1).ToDateTime(new TimeOnly(HardCloseHourEastern, 0)));

        if (nowUtc < windowStart)
        {
            return new PollerDecision(false, windowStart, cadence);
        }

        if (nowUtc >= hardClose)
        {
            return new PollerDecision(false, null, cadence);
        }

        DateTimeOffset nextPoll = nowUtc + TimeSpan.FromMinutes((int)cadence);
        return new PollerDecision(true, nextPoll, cadence);
    }
}
