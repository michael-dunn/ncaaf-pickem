using System.Globalization;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Push;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// The exact wording of the Feature 11 notification catalog. One method per catalog entry; the
/// <c>Body</c> string each returns is the catalog text with its placeholders filled in, so a test
/// asserting message text can compare directly against these methods' output.
/// </summary>
/// <remarks>
/// Tap targets are the picks page (<see cref="PicksUrl"/>) or league home
/// (<see cref="LeagueHomeUrl"/>), per the P7-03 card. <c>Tag</c> collapses a repeat of the same
/// notification for the same league/week on the device rather than stacking.
/// </remarks>
public static class NotificationMessages
{
    /// <summary>The picks page for one league's week.</summary>
    public static string PicksUrl(Guid leagueId, int week) =>
        string.Create(CultureInfo.InvariantCulture, $"/leagues/{leagueId}/weeks/{week}/picks");

    /// <summary>League home, where the roster lives.</summary>
    public static string LeagueHomeUrl(Guid leagueId) =>
        string.Create(CultureInfo.InvariantCulture, $"/leagues/{leagueId}");

    /// <summary>Catalog #1: Friday evening member reminder.</summary>
    /// <param name="week">The league week.</param>
    /// <param name="picksLeft">Active games the member has not yet picked.</param>
    /// <param name="lockAtUtc">The week's lock instant.</param>
    /// <param name="leagueId">The league.</param>
    public static PushPayload FridayMemberReminder(int week, int picksLeft, DateTimeOffset lockAtUtc, Guid leagueId) =>
        new(
            "Pick reminder",
            string.Create(
                CultureInfo.InvariantCulture,
                $"You have {picksLeft} picks left for Week {week}. Lock is Saturday at {FormatLockTime(lockAtUtc)}."),
            PicksUrl(leagueId, week),
            string.Create(CultureInfo.InvariantCulture, $"friday-reminder-{leagueId}-{week}"));

    /// <summary>Catalog #2: Friday evening commissioner summary.</summary>
    /// <param name="week">The league week.</param>
    /// <param name="unsubmittedCount">How many members have not submitted.</param>
    /// <param name="unsubmittedNames">Their effective display names, already comma-joined.</param>
    /// <param name="leagueId">The league.</param>
    public static PushPayload CommissionerSummary(int week, int unsubmittedCount, string unsubmittedNames, Guid leagueId) =>
        new(
            "Unsubmitted picks",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{unsubmittedCount} members haven't submitted Week {week} picks: {unsubmittedNames}."),
            LeagueHomeUrl(leagueId),
            string.Create(CultureInfo.InvariantCulture, $"commissioner-summary-{leagueId}-{week}"));

    /// <summary>Catalog #3: Saturday, one hour before lock.</summary>
    /// <param name="week">The league week.</param>
    /// <param name="picksLeft">Active games the member has not yet picked.</param>
    /// <param name="leagueId">The league.</param>
    public static PushPayload SaturdayReminder(int week, int picksLeft, Guid leagueId) =>
        new(
            "Picks lock soon",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Picks lock in 1 hour. You have {picksLeft} picks left for Week {week}."),
            PicksUrl(leagueId, week),
            string.Create(CultureInfo.InvariantCulture, $"saturday-reminder-{leagueId}-{week}"));

    /// <summary>Catalog #4: games added to a week the member had already submitted.</summary>
    /// <param name="week">The league week.</param>
    /// <param name="addedCount">How many games were added, coalesced per regeneration.</param>
    /// <param name="leagueId">The league.</param>
    /// <param name="weekGameSetId">The set the games were added to, for the collapse tag.</param>
    public static PushPayload GamesAdded(int week, int addedCount, Guid leagueId, Guid weekGameSetId) =>
        new(
            "New games added",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{addedCount} new games were added to Week {week}. Update your picks."),
            PicksUrl(leagueId, week),
            string.Create(CultureInfo.InvariantCulture, $"games-added-{weekGameSetId}"));

    /// <summary>Catalog #5: a game removed before lock.</summary>
    /// <param name="week">The league week.</param>
    /// <param name="awayTeamName">The away team's school name.</param>
    /// <param name="homeTeamName">The home team's school name.</param>
    /// <param name="leagueId">The league.</param>
    /// <param name="gameSetGameId">The removed row, for the collapse tag.</param>
    public static PushPayload GameRemoved(
        int week,
        string awayTeamName,
        string homeTeamName,
        Guid leagueId,
        Guid gameSetGameId) =>
        new(
            "Game removed",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{awayTeamName} vs {homeTeamName} was removed from Week {week}. Your other picks are unchanged."),
            PicksUrl(leagueId, week),
            string.Create(CultureInfo.InvariantCulture, $"game-removed-{gameSetGameId}"));

    /// <summary>"12:00 PM" for the lock time, in Eastern, as the catalog's "h:mm AM/PM" wants.</summary>
    private static string FormatLockTime(DateTimeOffset lockAtUtc) =>
        SeasonCalendar.ToEastern(lockAtUtc).ToString("h:mm tt", CultureInfo.InvariantCulture);
}
