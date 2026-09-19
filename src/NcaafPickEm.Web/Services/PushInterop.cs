using System.Text.Json.Serialization;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// JS-interop-only shapes returned by <c>wwwroot/js/push.js</c> (Feature 11, P7-02). Kept
/// separate from <c>NcaafPickEm.Shared.Contracts.Push</c>: those are the wire contract with the
/// Api, these three are purely what the browser's Push API hands back. Every property carries an
/// explicit <see cref="JsonPropertyNameAttribute"/> rather than relying on Blazor's default
/// interop JSON naming convention, so the mapping to the JS module's camelCase objects is
/// unambiguous.
/// </summary>
public sealed record PushSupportInfo(
    [property: JsonPropertyName("supported")] bool Supported,
    [property: JsonPropertyName("isIos")] bool IsIos,
    [property: JsonPropertyName("isStandalone")] bool IsStandalone,
    [property: JsonPropertyName("permission")] string Permission);

/// <summary>Result of <c>push.js</c>'s <c>subscribe(vapidPublicKey)</c>.</summary>
public sealed record PushSubscribeResult(
    [property: JsonPropertyName("granted")] bool Granted,
    [property: JsonPropertyName("endpoint")] string? Endpoint,
    [property: JsonPropertyName("p256dh")] string? P256dh,
    [property: JsonPropertyName("auth")] string? Auth,
    [property: JsonPropertyName("userAgent")] string? UserAgent);

/// <summary>The browser's current push subscription for this device, from <c>getExistingSubscription()</c>.</summary>
public sealed record PushSubscriptionInfo(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("p256dh")] string P256dh,
    [property: JsonPropertyName("auth")] string Auth,
    [property: JsonPropertyName("userAgent")] string? UserAgent);
