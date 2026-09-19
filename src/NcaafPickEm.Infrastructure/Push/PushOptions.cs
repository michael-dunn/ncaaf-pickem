namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The <c>Push:*</c> configuration section (01-Architecture.md): the VAPID key pair the browser
/// and the push service authenticate us with, plus the contact subject those services require.
/// </summary>
/// <remarks>
/// Generate a pair with <c>deploy/generate-vapid.ps1</c>. Every value is optional at the binding
/// level on purpose: the app must boot with the section missing or still holding the template
/// placeholders, and report the problem at the edge (503 from the public-key endpoint, a Failed
/// notification log row) rather than refusing to start. <see cref="VapidConfiguration"/> decides
/// whether what is here is actually usable.
/// </remarks>
public sealed class PushOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Push";

    /// <summary>Base64url-encoded P-256 public key, handed to the browser.</summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>Base64url-encoded P-256 private key. A secret; never leaves the server.</summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary><c>mailto:</c> or <c>https:</c> contact for the push service operator.</summary>
    public string? Subject { get; set; }
}
