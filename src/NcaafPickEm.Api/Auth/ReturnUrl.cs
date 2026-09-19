namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Makes a caller-supplied <c>returnUrl</c> safe to redirect to.
/// </summary>
public static class ReturnUrl
{
    /// <summary>
    /// Returns <paramref name="candidate"/> when it is a local path, otherwise
    /// <see cref="AuthDefaults.DefaultReturnUrl"/>.
    /// </summary>
    /// <remarks>
    /// An open redirect on the login route is how a phishing page borrows our domain, so the rule
    /// is deliberately strict: one leading slash, no scheme, and no protocol-relative <c>//host</c>
    /// or backslash variant that a browser would still treat as an absolute URL.
    /// </remarks>
    public static string Sanitize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return AuthDefaults.DefaultReturnUrl;
        }

        string trimmed = candidate.Trim();

        // A control character left in the middle of the value would go straight into the Location
        // header; CR/LF there is response splitting, and Kestrel would 500 the login instead
        // (P8-01).
        if (trimmed.Any(char.IsControl))
        {
            return AuthDefaults.DefaultReturnUrl;
        }

        bool isLocal = trimmed[0] is '/'
            && (trimmed.Length == 1 || (trimmed[1] is not '/' and not '\\'));

        return isLocal ? trimmed : AuthDefaults.DefaultReturnUrl;
    }
}
