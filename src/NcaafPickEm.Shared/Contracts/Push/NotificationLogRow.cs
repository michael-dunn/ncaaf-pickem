using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Push;

/// <summary>
/// One notification attempt, for the commissioner's "did the reminders go out?" view (Feature 11).
/// </summary>
/// <param name="CreatedUtc">When the send was attempted.</param>
/// <param name="UserDisplayName">The recipient's effective per-league name.</param>
/// <param name="Week">The week the notification was about.</param>
/// <param name="Type">Which notification it was.</param>
/// <param name="Result">How it ended.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record NotificationLogRow(
    DateTimeOffset CreatedUtc,
    string UserDisplayName,
    int Week,
    NotificationType Type,
    NotificationResult Result,
    string? Error);
