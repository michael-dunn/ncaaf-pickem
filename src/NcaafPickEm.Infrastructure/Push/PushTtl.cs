using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// How long a push service may hold a message for a device that is offline, per notification kind.
/// </summary>
/// <remarks>
/// The card fixes one hour for the reminders: a "you have picks left" nudge delivered after lock
/// is worse than no nudge at all, and all three reminders are at most an hour from something the
/// member can no longer change. The two event notifications get four hours because "games were
/// added to your week" stays actionable until lock, and a phone that was in a tunnel at kickoff-
/// minus-six-hours should still hear about it (see DECISIONS.md, P7-01).
/// </remarks>
public static class PushTtl
{
    /// <summary>TTL for FridayReminder, CommissionerSummary and SaturdayReminder.</summary>
    public static readonly TimeSpan Reminder = TimeSpan.FromHours(1);

    /// <summary>TTL for GamesAdded and GameRemoved.</summary>
    public static readonly TimeSpan Event = TimeSpan.FromHours(4);

    /// <summary>The TTL for a notification type.</summary>
    /// <param name="type">The notification kind.</param>
    public static TimeSpan For(NotificationType type) => type switch
    {
        NotificationType.FridayReminder or
        NotificationType.CommissionerSummary or
        NotificationType.SaturdayReminder => Reminder,
        _ => Event,
    };
}
