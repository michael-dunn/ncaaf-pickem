namespace NcaafPickEm.Shared.Contracts.Push;

/// <summary>
/// Whether the caller has a stored subscription for the device that is asking (Feature 11).
/// The settings page shows "Notifications are off on this device" when this is false.
/// </summary>
/// <param name="HasSubscriptionForThisDevice">
/// True when the queried endpoint is stored against the calling user.
/// </param>
public sealed record PushStatusResponse(bool HasSubscriptionForThisDevice);
