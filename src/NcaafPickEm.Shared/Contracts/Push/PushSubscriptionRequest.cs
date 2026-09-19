namespace NcaafPickEm.Shared.Contracts.Push;

/// <summary>
/// One browser's push subscription, as returned by <c>PushManager.subscribe</c> (Feature 11).
/// Upserted by endpoint: posting the same endpoint twice leaves exactly one row.
/// </summary>
/// <param name="Endpoint">The push service URL. Absolute https, at most 2048 characters.</param>
/// <param name="P256dh">The subscription's <c>keys.p256dh</c> value.</param>
/// <param name="Auth">The subscription's <c>keys.auth</c> value.</param>
/// <param name="UserAgent">Optional, purely so a member can tell their devices apart in a log.</param>
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? UserAgent);
