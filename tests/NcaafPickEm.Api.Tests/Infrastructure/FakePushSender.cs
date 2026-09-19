using System.Collections.Concurrent;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Infrastructure.Push;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A scriptable <see cref="IPushSender"/>: outcomes per endpoint, and a record of everything that
/// was "sent".
/// </summary>
/// <remarks>
/// Lives in the test project on purpose. The production fallback for missing keys is
/// <c>NullPushSender</c>; this one exists so a test can say "this device answers 410, that one
/// fails twice and then works" without a network.
/// </remarks>
public sealed class FakePushSender : IPushSender
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PushSendResult>> _scripts =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<SentPush> _sent = new();

    /// <summary>What every unscripted endpoint answers. Accepted by default.</summary>
    public PushSendResult DefaultOutcome { get; set; } = PushSendResult.Accepted(201);

    /// <summary>Everything the sender was asked to deliver, in order.</summary>
    public IReadOnlyCollection<SentPush> Sent => _sent;

    /// <summary>Queues the outcomes an endpoint answers, one per attempt.</summary>
    /// <param name="endpoint">The subscription endpoint.</param>
    /// <param name="results">Outcomes in order; the last one repeats once the queue drains.</param>
    public void Script(string endpoint, params PushSendResult[] results)
    {
        ArgumentNullException.ThrowIfNull(results);

        ConcurrentQueue<PushSendResult> queue = _scripts.GetOrAdd(endpoint, _ => new ConcurrentQueue<PushSendResult>());
        foreach (PushSendResult result in results)
        {
            queue.Enqueue(result);
        }
    }

    /// <summary>Forgets every script and every recorded send.</summary>
    public void Reset()
    {
        _scripts.Clear();
        _sent.Clear();
    }

    /// <summary>Everything sent to one endpoint.</summary>
    /// <param name="endpoint">The subscription endpoint.</param>
    public IReadOnlyList<SentPush> SentTo(string endpoint) =>
        [.. _sent.Where(push => string.Equals(push.Endpoint, endpoint, StringComparison.Ordinal))];

    /// <inheritdoc />
    public Task<PushSendResult> SendAsync(
        PushSubscription subscription,
        PushPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        _sent.Enqueue(new SentPush(subscription.Endpoint, payload, payload.ToJson(), ttl));

        PushSendResult result = DefaultOutcome;

        if (_scripts.TryGetValue(subscription.Endpoint, out ConcurrentQueue<PushSendResult>? queue))
        {
            if (queue.Count > 1)
            {
                queue.TryDequeue(out result);
            }
            else if (queue.TryPeek(out PushSendResult last))
            {
                result = last;
            }
        }

        return Task.FromResult(result);
    }

    /// <summary>One delivery the fake was asked to make.</summary>
    /// <param name="Endpoint">The device.</param>
    /// <param name="Payload">The payload object.</param>
    /// <param name="Json">Exactly what would have gone on the wire.</param>
    /// <param name="Ttl">The TTL the caller asked for.</param>
    public sealed record SentPush(string Endpoint, PushPayload Payload, string Json, TimeSpan Ttl);
}
