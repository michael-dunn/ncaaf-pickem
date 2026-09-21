namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Names and values the auth stack, the Blazor client, and the tests all have to agree on.
/// </summary>
public static class AuthDefaults
{
    /// <summary>Our session cookie. Feature 08: HttpOnly, Secure, SameSite=Lax, 90-day sliding.</summary>
    public const string CookieName = "ncaaf.auth";

    /// <summary>How long a session survives inactivity. Feature 08 asks for at least 90 days.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(90);

    /// <summary>Header every mutating <c>/api</c> call must carry (01-Architecture.md, CSRF).</summary>
    public const string CsrfHeaderName = "X-Requested-With";

    /// <summary>The only accepted value of <see cref="CsrfHeaderName"/>.</summary>
    public const string CsrfHeaderValue = "NcaafPickEm";

    /// <summary>Route the Google handler owns. Must match the OAuth client's redirect URI.</summary>
    public const string GoogleCallbackPath = "/auth/callback/google";

    /// <summary>
    /// Identity header <c>tailscale serve</c> injects: the tailnet login of the calling device's
    /// user (an email for Google and Microsoft accounts, <c>name@github</c> for a GitHub one).
    /// Trusted exactly as it arrives; the tailnet is the security boundary (Phase 9, Q4).
    /// </summary>
    public const string TailscaleLoginHeader = "Tailscale-User-Login";

    /// <summary>
    /// Identity header carrying the tailnet user's profile name. Optional, and RFC 2047 encoded
    /// when it is not plain ASCII (see <see cref="Rfc2047"/>).
    /// </summary>
    public const string TailscaleNameHeader = "Tailscale-User-Name";

    /// <summary>Scheme name of <see cref="TailscaleAuthenticationHandler"/>.</summary>
    public const string TailscaleScheme = "Tailscale";

    /// <summary>
    /// The default scheme: a policy scheme that forwards to <see cref="TailscaleScheme"/> when
    /// <see cref="TailscaleLoginHeader"/> is present and non-empty, and to the cookie otherwise.
    /// </summary>
    public const string SelectorScheme = "AppAuth";

    /// <summary>Where a caller lands after signing in when no usable <c>returnUrl</c> was given.</summary>
    public const string DefaultReturnUrl = "/";

    /// <summary>Key under which <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> holds the resolved membership.</summary>
    public const string MembershipItemKey = "NcaafPickEm.Membership";
}
