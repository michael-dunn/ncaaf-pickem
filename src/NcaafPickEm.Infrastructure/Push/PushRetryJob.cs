using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// Drains <c>PushRetries</c>: the "retry 3 times over 15 minutes, then Failed" half of the
/// Feature 11 delivery policy.
/// </summary>
/// <remarks>
/// <para>
/// A one-shot job (P0-06) rather than an in-process <c>Task.Delay</c>: the due time lives in the
/// database, so a restart in the middle of the fifteen-minute window still delivers, and the
/// scheduler's <c>JobRuns</c> claim keeps two ticks from sending the same message twice.
/// </para>
/// <para>
/// The <c>JobRuns</c> key is <c>PushRetry:{retryId:N}</c> — 42 characters, inside the 60 the
/// scheduler allows — and <c>ScheduledForUtc</c> is the row's <c>NextAttemptUtc</c>, which moves
/// on every attempt, so each attempt is genuinely its own occurrence. A row whose attempt was
/// claimed but never finished (a crash between the two) would otherwise be deduplicated forever,
/// so an occurrence more than <see cref="StaleAfter"/> late is re-offered on a rounded-down
/// quarter-hour instead, which produces a fresh key.
/// </para>
/// </remarks>
public sealed class PushRetryJob : IOneShotJob
{
    /// <summary>A due time older than this is treated as an abandoned attempt and re-offered.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private const int MaxPerTick = 200;

    private readonly AppDbContext _database;
    private readonly IPushSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PushRetryJob> _logger;

    /// <summary>Creates the job.</summary>
    /// <param name="database">The context.</param>
    /// <param name="sender">The transport.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Log sink.</param>
    public PushRetryJob(
        AppDbContext database,
        IPushSender sender,
        TimeProvider timeProvider,
        ILogger<PushRetryJob> logger)
    {
        _database = database;
        _sender = sender;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "PushRetry";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        DateTime cutoff = nowUtc.UtcDateTime;

        var due = await _database.PushRetries
            .AsNoTracking()
            .Where(retry => retry.NextAttemptUtc <= cutoff)
            .OrderBy(retry => retry.NextAttemptUtc)
            .Take(MaxPerTick)
            .Select(retry => new { retry.Id, retry.NextAttemptUtc })
            .ToListAsync(cancellationToken);

        var occurrences = new List<OneShotOccurrence>(due.Count);

        foreach (var row in due)
        {
            var dueUtc = new DateTimeOffset(DateTime.SpecifyKind(row.NextAttemptUtc, DateTimeKind.Utc));
            DateTimeOffset scheduledFor = nowUtc - dueUtc > StaleAfter ? FloorToQuarterHour(nowUtc) : dueUtc;

            occurrences.Add(new OneShotOccurrence(row.Id.ToString("N"), scheduledFor));
        }

        return occurrences;
    }

    /// <inheritdoc />
    public async Task RunAsync(OneShotOccurrence occurrence, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(occurrence.Key, "N", out Guid retryId))
        {
            _logger.LogWarning("Ignoring a push retry occurrence with an unreadable key {Key}", occurrence.Key);
            return;
        }

        PushRetry? retry = await _database.PushRetries
            .Include(row => row.Subscription)
            .Include(row => row.NotificationLogEntry)
            .FirstOrDefaultAsync(row => row.Id == retryId, cancellationToken);

        if (retry is null)
        {
            // Already resolved by an earlier attempt, or the subscription was deleted under it.
            return;
        }

        if (retry.Subscription is null)
        {
            _database.PushRetries.Remove(retry);
            await _database.SaveChangesAsync(cancellationToken);
            return;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        int attemptsMade = retry.Attempt + 1;

        PushSendResult result = await SendAsync(retry, cancellationToken);

        if (result.Success)
        {
            await SucceedAsync(retry, nowUtc, attemptsMade, cancellationToken);
            return;
        }

        if (result.SubscriptionGone)
        {
            await ExpireAsync(retry, result, cancellationToken);
            return;
        }

        await FailAsync(retry, result, attemptsMade, cancellationToken);
    }

    private static DateTimeOffset FloorToQuarterHour(DateTimeOffset instant)
    {
        const long quarterHour = TimeSpan.TicksPerMinute * 15;
        return new DateTimeOffset(instant.UtcTicks - (instant.UtcTicks % quarterHour), TimeSpan.Zero);
    }

    /// <summary>A retry never downgrades a row another device already delivered.</summary>
    private static bool CanUpdate(NotificationLogEntry? entry) =>
        entry is not null && entry.Result != NotificationResult.Sent;

    private async Task<PushSendResult> SendAsync(PushRetry retry, CancellationToken cancellationToken)
    {
        PushPayload payload = PushPayload.FromJson(retry.PayloadJson);
        var ttl = TimeSpan.FromSeconds(retry.TtlSeconds);

        try
        {
            return await _sender.SendAsync(retry.Subscription!, payload, ttl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transport that throws is a retryable failure, not a job crash.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(
                exception,
                "Push transport threw retrying subscription {SubscriptionId}",
                retry.SubscriptionId);

            return PushSendResult.Failed($"The push transport threw: {exception.Message}");
        }
    }

    private async Task SucceedAsync(PushRetry retry, DateTime nowUtc, int attemptsMade, CancellationToken cancellationToken)
    {
        retry.Subscription!.LastSuccessUtc = nowUtc;
        retry.Subscription.FailureCount = 0;

        if (CanUpdate(retry.NotificationLogEntry))
        {
            retry.NotificationLogEntry!.Result = NotificationResult.Sent;
            retry.NotificationLogEntry.Error = null;
            retry.NotificationLogEntry.SubscriptionId ??= retry.SubscriptionId;
        }

        _database.PushRetries.Remove(retry);
        await _database.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Push to subscription {SubscriptionId} succeeded on retry {Attempt} of {MaxAttempts}",
            retry.SubscriptionId,
            attemptsMade,
            PushRetryPolicy.MaxAttempts);
    }

    private async Task ExpireAsync(PushRetry retry, PushSendResult result, CancellationToken cancellationToken)
    {
        if (CanUpdate(retry.NotificationLogEntry))
        {
            retry.NotificationLogEntry!.Result = NotificationResult.Expired;
            retry.NotificationLogEntry.Error = PushDelivery.Truncate(result.Error);
        }

        Guid subscriptionId = retry.SubscriptionId;

        _database.PushRetries.Remove(retry);
        await _database.SaveChangesAsync(cancellationToken);
        await PushDelivery.RemoveSubscriptionsAsync(_database, [subscriptionId], cancellationToken);

        _logger.LogInformation("Push subscription {SubscriptionId} expired during retry; deleted", subscriptionId);
    }

    private async Task FailAsync(PushRetry retry, PushSendResult result, int attemptsMade, CancellationToken cancellationToken)
    {
        retry.Subscription!.FailureCount++;

        DateTime? nextAttemptUtc = PushRetryPolicy.NextAttemptUtc(retry.CreatedUtc, attemptsMade);

        if (nextAttemptUtc is null)
        {
            if (CanUpdate(retry.NotificationLogEntry))
            {
                retry.NotificationLogEntry!.Result = NotificationResult.Failed;
                retry.NotificationLogEntry.Error = PushDelivery.Truncate(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{result.Error} Gave up after {PushRetryPolicy.MaxAttempts} retries."));
            }

            _database.PushRetries.Remove(retry);
            await _database.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Push to subscription {SubscriptionId} failed after {MaxAttempts} retries: {Error}",
                retry.SubscriptionId,
                PushRetryPolicy.MaxAttempts,
                result.Error);

            return;
        }

        retry.Attempt = attemptsMade;
        retry.NextAttemptUtc = nextAttemptUtc.Value;

        if (CanUpdate(retry.NotificationLogEntry))
        {
            retry.NotificationLogEntry!.Result = NotificationResult.Failed;
            retry.NotificationLogEntry.Error = PushDelivery.Truncate(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Error} Retry {attemptsMade + 1} of {PushRetryPolicy.MaxAttempts} is due at {nextAttemptUtc.Value:o}."));
        }

        await _database.SaveChangesAsync(cancellationToken);
    }
}
