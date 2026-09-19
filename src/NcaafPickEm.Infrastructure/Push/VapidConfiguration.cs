using WebPush;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The validated VAPID key pair, or the reason there isn't one.
/// </summary>
/// <remarks>
/// Registered as a singleton so both the sender and <c>GET /api/push/vapid-public-key</c> ask the
/// same object. Validation happens once, here, using the library's own key validators, so a
/// template placeholder such as <c>&lt;dev-vapid-public-key&gt;</c> is treated exactly like a
/// missing key: the app boots, the endpoint answers 503, and every send reports Failed.
/// </remarks>
public sealed class VapidConfiguration
{
    private VapidConfiguration(VapidDetails? details, string? error)
    {
        Details = details;
        Error = error;
    }

    /// <summary>True when a usable key pair and subject are configured.</summary>
    public bool IsConfigured => Details is not null;

    /// <summary>The public key to hand the browser, or null when nothing usable is configured.</summary>
    public string? PublicKey => Details?.PublicKey;

    /// <summary>Why <see cref="IsConfigured"/> is false, in words an operator can act on.</summary>
    public string? Error { get; }

    /// <summary>The details the WebPush client signs with. Null when unconfigured.</summary>
    internal VapidDetails? Details { get; }

    /// <summary>Validates <paramref name="options"/> and returns the result either way.</summary>
    /// <param name="options">The bound <c>Push</c> section.</param>
    public static VapidConfiguration FromOptions(PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.VapidPublicKey)
            || string.IsNullOrWhiteSpace(options.VapidPrivateKey)
            || string.IsNullOrWhiteSpace(options.Subject))
        {
            return Unconfigured(
                "Push:VapidPublicKey, Push:VapidPrivateKey and Push:Subject are not all set. "
                + "Run deploy/generate-vapid.ps1 and set the three Push__* values.");
        }

        var details = new VapidDetails(options.Subject, options.VapidPublicKey, options.VapidPrivateKey);

        try
        {
            VapidHelper.ValidateSubject(details.Subject);
            VapidHelper.ValidatePublicKey(details.PublicKey);
            VapidHelper.ValidatePrivateKey(details.PrivateKey);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Unconfigured(
                $"Push:* is set but not a valid VAPID configuration: {exception.Message} "
                + "Run deploy/generate-vapid.ps1 for a fresh key pair.");
        }

        return new VapidConfiguration(details, error: null);
    }

    /// <summary>An explicitly unconfigured instance, for tests and for the null sender.</summary>
    /// <param name="error">The reason to report.</param>
    public static VapidConfiguration Unconfigured(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new VapidConfiguration(details: null, error);
    }
}
