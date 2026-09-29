using System.Globalization;
using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays the scheduler's calendar: runs any of the app's jobs now, against the shared database,
/// with the same classes the app's <c>JobScheduler</c> runs - "it is Friday at 8 PM" on a Tuesday
/// morning is simply the Friday reminder job's <c>RunAsync</c>.
/// </summary>
/// <remarks>
/// A scheduled job runs as its own cron minute would. A one-shot job runs whatever it reports due
/// now - except the last-call reminder, which is looked up a week ahead so it can be sent before
/// "lock minus an hour" arrives. Its own guard still refuses once the lock has passed, and the
/// notification log's once-per-week rule still applies (rewind the week to send it again).
/// </remarks>
public sealed class JobDesk
{
    /// <summary>The jobs worth pressing, in the order a week meets them.</summary>
    public static readonly IReadOnlyList<SimJob> Jobs =
    [
        new("EnsureCurrentWeekSets", "Sun 12:05 AM ET", "Creates and generates this week's set for every league that has none."),
        new("RegenerateGameSets", "Tue 3:30 AM ET", "Regenerates every open week from its rules: new games added, dropped ones removed."),
        new("FridayMemberReminder", "Fri 8:00 PM ET", "Pushes 'picks due' to members who have not submitted."),
        new("FridayCommissionerSummary", "Fri 9:00 PM ET", "Pushes commissioners who still has not submitted."),
        new("SaturdayReminder", "Lock - 1 h", "Pushes the last call now, to members who have not submitted."),
        new("LockWeek", "Every minute", "Locks every week whose first kickoff has passed."),
        new("NightlyRescore", "4:30 AM ET", "Rescores every locked week that is not complete."),
        new("PushRetry", "Every minute", "Retries failed pushes that are due."),
    ];

    private static readonly TimeSpan LastCallLookAhead = TimeSpan.FromDays(8);

    private readonly IEnumerable<IScheduledJob> _scheduled;
    private readonly IEnumerable<IOneShotJob> _oneShots;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the desk over the step's scope's jobs.</summary>
    public JobDesk(IEnumerable<IScheduledJob> scheduled, IEnumerable<IOneShotJob> oneShots, TimeProvider timeProvider)
    {
        _scheduled = scheduled;
        _oneShots = oneShots;
        _timeProvider = timeProvider;
    }

    /// <summary>Runs the named job now.</summary>
    /// <returns>What happened, for the activity log.</returns>
    /// <exception cref="InvalidOperationException">No job has that name.</exception>
    public async Task<string> RunAsync(string name, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        if (_scheduled.FirstOrDefault(job => job.Name == name) is IScheduledJob scheduled)
        {
            await scheduled.RunAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            return $"{name} ran";
        }

        if (_oneShots.FirstOrDefault(job => job.Name == name) is IOneShotJob oneShot)
        {
            TimeSpan lookAhead = name == "SaturdayReminder" ? LastCallLookAhead : TimeSpan.Zero;
            IReadOnlyList<OneShotOccurrence> due = await oneShot.GetDueAsync(nowUtc + lookAhead, cancellationToken).ConfigureAwait(false);

            foreach (OneShotOccurrence occurrence in due)
            {
                await oneShot.RunAsync(occurrence, cancellationToken).ConfigureAwait(false);
            }

            return string.Create(CultureInfo.InvariantCulture, $"{name} ran {due.Count} occurrence(s)");
        }

        throw new InvalidOperationException($"The app has no job named '{name}'.");
    }
}
