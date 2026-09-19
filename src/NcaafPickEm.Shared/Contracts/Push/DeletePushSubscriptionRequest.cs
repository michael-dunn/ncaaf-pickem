namespace NcaafPickEm.Shared.Contracts.Push;

/// <summary>
/// Body of <c>DELETE /api/push/subscriptions</c> (Feature 11). Idempotent: deleting an endpoint
/// that is not stored, or one owned by somebody else, still answers 204.
/// </summary>
/// <param name="Endpoint">The push service URL to forget.</param>
public sealed record DeletePushSubscriptionRequest(string Endpoint);
