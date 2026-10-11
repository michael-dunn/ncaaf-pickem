using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Tuesday 04:00 AM Eastern (Feature 16, D-190): the moment the current week's picks open, half
/// an hour after <see cref="RegenerateGameSetsJob"/> settled the set. Every active league whose
/// current week has games gets a "Week N picks are open" push to each member; a league whose week
/// has no games (generation failed, or its rules select nothing) stays closed, and its
/// commissioners get an alert instead.
/// </summary>
/// <remarks>
/// The job only notifies: picks open by the clock through <see cref="PicksOpenRule"/>, whether or
/// not this job runs. Members without a push subscription are logged and skipped by
/// <see cref="NotificationService"/>. One league's failure does not cost the others their push.
/// </remarks>
public sealed class PicksOpenJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly ISeasonWeekSource _weekSource;
    private readonly NotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PicksOpenJob> _logger;

    /// <summary>Creates the job.</summary>
    public PicksOpenJob(
        AppDbContext database,
        ISeasonWeekSource weekSource,
        NotificationService notifications,
        TimeProvider timeProvider,
        ILogger<PicksOpenJob> logger)
    {
        _database = database;
        _weekSource = weekSource;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "PicksOpen";

    /// <inheritdoc />
    public string CronExpression => "0 4 * * 2";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        List<League> leagues = await _database.Leagues
            .AsNoTracking()
            .Where(league => !league.IsComplete)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int opened = 0;
        int notSet = 0;
        int sent = 0;
        int leaguesFailed = 0;

        foreach (League league in leagues)
        {
            IReadOnlyList<SeasonWeek> weeks = await _weekSource
                .GetWeeksAsync(league.SeasonYear, cancellationToken)
                .ConfigureAwait(false);
            if (weeks.Count == 0)
            {
                continue;
            }

            CurrentWeek current = SeasonCalendar.CurrentWeekAt(nowUtc, weeks);
            int week = current.Week;
            if (current.State != SeasonState.InSeason || week < league.FirstWeek || week > league.LastWeek)
            {
                continue;
            }

            try
            {
                bool hasActiveGames = await _database.WeekGameSetGames
                    .AsNoTracking()
                    .AnyAsync(
                        row => row.WeekGameSet!.LeagueId == league.Id
                            && row.WeekGameSet.Week == week
                            && !row.IsRemoved
                            && !row.IsVoided,
                        cancellationToken)
                    .ConfigureAwait(false);

                PicksOpening opening = PicksOpenRule.At(nowUtc, weeks.First(candidate => candidate.Week == week), hasActiveGames);
                if (opening.State == PicksOpenState.NotYetOpen)
                {
                    // A run that came early (a manual trigger, a clock adjustment): nothing to say yet.
                    continue;
                }

                bool open = opening.IsOpen;
                NotificationType type = open ? NotificationType.PicksOpen : NotificationType.PicksNotSet;
                PushPayload payload = open
                    ? NotificationMessages.PicksOpen(week, league.Id)
                    : NotificationMessages.PicksNotSet(week, league.Id);

                List<Guid> recipients = await _database.Memberships
                    .AsNoTracking()
                    .Where(membership => membership.LeagueId == league.Id
                        && membership.RemovedUtc == null
                        && (open || membership.Role == MembershipRole.Commissioner))
                    .Select(membership => membership.UserId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (open)
                {
                    opened++;
                }
                else
                {
                    notSet++;
                    _logger.LogWarning(
                        "League {LeagueId} week {Week} has no games at its picks-open moment; picks stay closed and {Count} commissioner(s) are alerted.",
                        league.Id,
                        week,
                        recipients.Count);
                }

                foreach (Guid userId in recipients)
                {
                    NotificationResult result = await _notifications
                        .SendToUserAsync(userId, type, league.Id, week, payload, ttl: null, cancellationToken)
                        .ConfigureAwait(false);
                    if (result == NotificationResult.Sent)
                    {
                        sent++;
                    }
                }
            }
#pragma warning disable CA1031 // Whatever went wrong for this league, the next one still gets its push.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                leaguesFailed++;
                _database.ChangeTracker.Clear();
                _logger.LogError(
                    exception,
                    "{JobName}: league {LeagueId} week {Week} failed; continuing with the remaining leagues",
                    Name,
                    league.Id,
                    week);
            }
        }

        _logger.LogInformation(
            "{JobName} for occurrence {ScheduledFor:o}: {Opened} league week(s) opened, {NotSet} without games, {Sent} push(es) sent, {Failed} league(s) errored",
            Name,
            scheduledFor,
            opened,
            notSet,
            sent,
            leaguesFailed);
    }
}
