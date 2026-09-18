namespace NcaafPickEm.Web.Services;

/// <summary>
/// Adds the CSRF marker header every mutating <c>/api</c> call must carry
/// (<c>X-Requested-With: NcaafPickEm</c>, see 01-Architecture.md and 03-API-Contracts.md).
/// The server rejects mutations without it with 400.
/// </summary>
public sealed class CsrfHeaderHandler : DelegatingHandler
{
    /// <summary>The header name the API's CSRF endpoint filter looks for.</summary>
    public const string HeaderName = "X-Requested-With";

    /// <summary>The header value the API's CSRF endpoint filter requires.</summary>
    public const string HeaderValue = "NcaafPickEm";

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsMutation(request.Method) && !request.Headers.Contains(HeaderName))
        {
            request.Headers.Add(HeaderName, HeaderValue);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsMutation(HttpMethod method) =>
        method == HttpMethod.Post
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Patch;
}
