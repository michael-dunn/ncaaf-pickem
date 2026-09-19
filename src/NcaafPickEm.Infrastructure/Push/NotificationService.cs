using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// Sends one notification to one member, across every device they have subscribed (Feature 11).
/// This is the only entry point the reminder jobs and event handlers (P7-03) use.
/// </summary>
/// <remarks>
/// <para>
/// <b>One log row per send, not per device.</b> The once-per-week guarantee is the filtered unique
/// index on <c>NotificationLog (UserId, LeagueId, Week, Type)</c>, which permits exactly one row
/// per member, league, week and reminder type — so a member with a phone and a laptop cannot have
/// two rows for Friday's reminder. The row is therefore the record of the *notification*, and its
/// <c>Result</c> is the best outcome across the member's devices:
/// <c>Sent</c> (at least one device accepted) beats <c>Failed</c> (at least one device is retrying
/// or has run out of retries) beats <c>Expired</c> (every device was gone and has been deleted)
/// beats <c>Skipped</c> (the member has no device, or this notification was already sent this
/// week). Per-device detail lives in the structured log.
/// </para>
/// <para>
/// <b>The row is written before anything is sent.</b> A duplicate-key violation on that insert is
/// the once-per-week check firing, and the answer is <see cref="NotificationResult.Skipped"/> with
/// nothing sent. It also means a crash mid-send leaves an honest <c>Skipped</c> row rather than a
/// claim that something was delivered.
/// </para>
/// <para>
/// <b>A Failed row can still become Sent.</b> When a device fails for a retryable reason the row
/// is marked <c>Failed</c> immediately, with an error that names the scheduled retry, and a
/// <c>PushRetries</c> row is queued; <see cref="PushRetryJob"/> promotes the row to <c>Sent</c> if
/// an attempt succeeds, and replaces the error text when the third attempt fails. A row that is
/// already <c>Sent</c> is never downgraded.
/// </para>
/// </remarks>
public sealed class NotificationService
{
    private readonly AppDbContext _database;
    private readonly IPushSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="database">The context.</param>
    /// <param name="sender">The transport.</param>
    /// <param name="timeProvider">The clock (05-Conventions.md).</param>
    /// <param name="logger">Log sink.</param>
    public NotificationService(
        AppDbContext database,
        IPushSender sender,
        TimeProvider timeProvider,
        ILogger<NotificationService> logger)
    {
        _database = database;
        _sender = sender;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Sends <paramref name="content"/> to every device <paramref name="userId"/> has subscribed,
    /// recording the attempt in <c>NotificationLog</c>.
    /// </summary>
    /// <param name="userId">The recipient.</param>
    /// <param name="type">Which notification this is; decides once-per-week and the default TTL.</param>
    /// <param name="leagueId">The league the notification is about.</param>
    /// <param name="week">The week the notification is about.</param>
    /// <param name="content">Title, body, tap target and collapse tag.</param>
    /// <param name="ttl">Overrides <see cref="PushTtl.For"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The value written to the log row.</returns>
    public async Task<NotificationResult> SendToUserAsync(
        Guid userId,
        NotificationType type,
        Guid leagueId,
        int week,
        PushPayload content,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        TimeSpan effectiveTtl = ttl ?? PushTtl.For(type);

        NotificationLogEntry? entry = await ClaimAsync(userId, type, leagueId, week, nowUtc, cancellationToken);
        if (entry is null)
        {
            _logger.LogInformation(
                "{NotificationType} for user {UserId} in league {LeagueId} week {Week} was already sent this week; skipping",
                type,
                userId,
                leagueId,
                week);

            return NotificationResult.Skipped;
        }

        List<PushSubscription> subscriptions = await _database.PushSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            entry.Error = "The member has no push subscription on any device.";
            await _database.SaveChangesAsync(cancellationToken);
            return NotificationResult.Skipped;
        }

        var outcome = new FanOutOutcome();
        string payloadJson = content.ToJson();

        foreach (PushSubscription subscription in subscriptions)
        {
            PushSendResult result = await SendOnceAsync(subscription, content, effectiveTtl, cancellationToken);
            Apply(entry, subscription, result, payloadJson, effectiveTtl, nowUtc, outcome);
        }

        entry.Result = outcome.Resolve();
        entry.Error = PushDelivery.Truncate(outcome.Error);
        entry.SubscriptionId = outcome.DeliveredTo;

        await _database.SaveChangesAsync(cancellationToken);
        await PushDelivery.RemoveSubscriptionsAsync(_database, outcome.Gone, cancellationToken);

        _logger.LogInformation(
            "{NotificationType} for user {UserId} in league {LeagueId} week {Week} across {DeviceCount} device(s): {Result}",
            type,
            userId,
            leagueId,
            week,
            subscriptions.Count,
            entry.Result);

        return entry.Result;
    }

    /// <summary>
    /// Inserts the log row that reserves this notification, or null when the filtered unique index
    /// says it already went out this week.
    /// </summary>
    private async Task<NotificationLogEntry?> ClaimAsync(
        Guid userId,
        NotificationType type,
        Guid leagueId,
        int week,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var entry = new NotificationLogEntry
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            LeagueId = leagueId,
            Week = week,
            Type = type,
            Result = NotificationResult.Skipped,
            CreatedUtc = nowUtc,
        };

        _database.NotificationLog.Add(entry);

        try
        {
            await _database.SaveChangesAsync(cancellationToken);
            return entry;
        }
        catch (DbUpdateException exception) when (PushDelivery.IsDuplicateKey(exception))
        {
            _database.Entry(entry).State = EntityState.Detached;
            return null;
        }
    }

    private void Apply(
        NotificationLogEntry entry,
        PushSubscription subscription,
        PushSendResult result,
        string payloadJson,
        TimeSpan ttl,
        DateTime nowUtc,
        FanOutOutcome outcome)
    {
        if (result.Success)
        {
            subscription.LastSuccessUtc = nowUtc;
            subscription.FailureCount = 0;
            outcome.RecordSent(subscription.Id);
            return;
        }

        if (result.SubscriptionGone)
        {
            outcome.RecordExpired(subscription.Id, result.Error);
            return;
        }

        subscription.FailureCount++;

        DateTime nextAttemptUtc = PushRetryPolicy.NextAttemptUtc(nowUtc, attempt: 0)!.Value;

        _database.PushRetries.Add(new PushRetry
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            NotificationLogId = entry.Id,
            PayloadJson = payloadJson,
            TtlSeconds = (int)Math.Clamp(ttl.TotalSeconds, 0, int.MaxValue),
            Attempt = 0,
            NextAttemptUtc = nextAttemptUtc,
            CreatedUtc = nowUtc,
        });

        outcome.RecordRetrying(
            $"{result.Error} Retry 1 of {PushRetryPolicy.MaxAttempts} is due at {nextAttemptUtc:o}.");
    }

    private async Task<PushSendResult> SendOnceAsync(
        PushSubscription subscription,
        PushPayload content,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _sender.SendAsync(subscription, content, ttl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transport that throws must not take the whole fan-out with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(
                exception,
                "Push transport threw for subscription {SubscriptionId}; treating it as a retryable failure",
                subscription.Id);

            return PushSendResult.Failed($"The push transport threw: {exception.Message}");
        }
    }

    /// <summary>Accumulates per-device outcomes into the single row's verdict.</summary>
    private sealed class FanOutOutcome
    {
        private readonly List<Guid> _gone = [];
        private bool _anySent;
        private bool _anyRetrying;

        public IReadOnlyCollection<Guid> Gone => _gone;

        public Guid? DeliveredTo { get; private set; }

        public string? Error { get; private set; }

        public void RecordSent(Guid subscriptionId)
        {
            _anySent = true;
            DeliveredTo ??= subscriptionId;
        }

        public void RecordExpired(Guid subscriptionId, string? error)
        {
            _gone.Add(subscriptionId);
            Error ??= error;
        }

        public void RecordRetrying(string? error)
        {
            _anyRetrying = true;

            // A pending retry is the more actionable error, so it replaces an expiry message.
            Error = error;
        }

        public NotificationResult Resolve()
        {
            if (_anySent)
            {
                return NotificationResult.Sent;
            }

            if (_anyRetrying)
            {
                return NotificationResult.Failed;
            }

            return _gone.Count > 0 ? NotificationResult.Expired : NotificationResult.Skipped;
        }
    }
}
