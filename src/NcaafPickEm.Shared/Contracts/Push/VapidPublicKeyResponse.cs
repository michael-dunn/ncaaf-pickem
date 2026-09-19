namespace NcaafPickEm.Shared.Contracts.Push;

/// <summary>
/// The server's VAPID application public key, which the browser needs before it can call
/// <c>PushManager.subscribe</c> (Feature 11).
/// </summary>
/// <param name="PublicKey">Base64url-encoded P-256 public key.</param>
public sealed record VapidPublicKeyResponse(string PublicKey);
