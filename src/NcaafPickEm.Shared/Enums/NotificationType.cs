namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Kind of web-push notification (Feature 11).
/// </summary>
/// <remarks>
/// The first three values are the once-per-week reminders enforced by the filtered unique index
/// on <c>NotificationLog (UserId, LeagueId, Week, Type)</c>. Keep them contiguous from zero:
/// the index filter lists their numeric values.
/// </remarks>
public enum NotificationType : byte
{
    /// <summary>Friday evening nudge to anyone who has not submitted.</summary>
    FridayReminder = 0,

    /// <summary>Friday evening roll-up for commissioners.</summary>
    CommissionerSummary = 1,

    /// <summary>Saturday morning last call before lock.</summary>
    SaturdayReminder = 2,

    /// <summary>Games were added to a week that a member had already submitted.</summary>
    GamesAdded = 3,

    /// <summary>A game was removed from a week before lock.</summary>
    GameRemoved = 4,

    /// <summary>Tuesday 04:00 ET: the week's picks are open (Feature 16).</summary>
    PicksOpen = 5,

    /// <summary>
    /// Tuesday 04:00 ET, to commissioners: the week's set has no games, so picks stay closed until
    /// it is generated (Feature 16).
    /// </summary>
    PicksNotSet = 6,
}
