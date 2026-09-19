using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The delivery bookkeeping <see cref="NotificationService"/>, <see cref="PushRetryJob"/> and the
/// unsubscribe endpoint share.
/// </summary>
public static class PushDelivery
{
    /// <summary>True when a save failed because a unique index rejected the row.</summary>
    /// <param name="exception">The save failure.</param>
    internal static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    /// <summary>Cuts an error down to what <c>NotificationLog.Error</c> can hold.</summary>
    /// <param name="error">The message, possibly null or oversized.</param>
    internal static string? Truncate(string? error) =>
        error is null || error.Length <= NotificationLogEntry.ErrorMaxLength
            ? error
            : error[..NotificationLogEntry.ErrorMaxLength];

    /// <summary>
    /// Deletes expired subscriptions, first detaching everything that points at them.
    /// </summary>
    /// <remarks>
    /// Every foreign key in this schema is <c>Restrict</c> (D-018), so the history rows in
    /// <c>NotificationLog</c> would block the delete. They are kept — a commissioner still wants
    /// to see that a reminder went to a device that has since expired — with their
    /// <c>SubscriptionId</c> nulled, which is exactly why the column is nullable.
    /// </remarks>
    /// <param name="database">The context.</param>
    /// <param name="subscriptionIds">Subscriptions the push service said are gone.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task RemoveSubscriptionsAsync(
        AppDbContext database,
        IReadOnlyCollection<Guid> subscriptionIds,
        CancellationToken cancellationToken)
    {
        if (subscriptionIds.Count == 0)
        {
            return;
        }

        await database.NotificationLog
            .Where(entry => entry.SubscriptionId != null && subscriptionIds.Contains(entry.SubscriptionId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.SubscriptionId, (Guid?)null),
                cancellationToken);

        await database.PushRetries
            .Where(retry => subscriptionIds.Contains(retry.SubscriptionId))
            .ExecuteDeleteAsync(cancellationToken);

        await database.PushSubscriptions
            .Where(subscription => subscriptionIds.Contains(subscription.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
