namespace NcaafPickEm.Api.Validation;

/// <summary>
/// The one definition of a well-formed push endpoint, shared by the subscribe and unsubscribe
/// validators so the two cannot disagree about what the client may send.
/// </summary>
public static class PushEndpointRules
{
    /// <summary>True when <paramref name="endpoint"/> is an absolute https URL.</summary>
    /// <param name="endpoint">The candidate endpoint.</param>
    public static bool IsAbsoluteHttps(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
