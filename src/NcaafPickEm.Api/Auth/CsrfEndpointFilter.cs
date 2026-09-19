namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Rejects a mutating <c>/api</c> call that does not carry
/// <c>X-Requested-With: NcaafPickEm</c> (01-Architecture.md, CSRF).
/// </summary>
/// <remarks>
/// The session cookie is <c>SameSite=Lax</c>, so a cross-site form post never carries it in the
/// first place; this header is the second lock, and it is one a form post cannot set at all
/// without a preflight the browser will refuse. Applied once to the whole <c>/api</c> group in
/// <c>EndpointMapping</c>. Health lives at the root and is exempt.
/// </remarks>
public sealed class CsrfEndpointFilter : IEndpointFilter
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpRequest request = context.HttpContext.Request;

        bool isMutation = MutatingMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase);
        if (!isMutation)
        {
            return await next(context);
        }

        bool hasHeader = request.Headers.TryGetValue(AuthDefaults.CsrfHeaderName, out var values)
            && values.Contains(AuthDefaults.CsrfHeaderValue, StringComparer.Ordinal);

        if (!hasHeader)
        {
            return TypedResults.Problem(
                title: "Missing request header",
                detail: $"Mutating requests must send {AuthDefaults.CsrfHeaderName}: {AuthDefaults.CsrfHeaderValue}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}

/// <summary>
/// Applies <see cref="CsrfEndpointFilter"/> and records that it is applied.
/// </summary>
public static class CsrfEndpointExtensions
{
    /// <summary>
    /// Adds <see cref="CsrfEndpointFilter"/> to <paramref name="builder"/> together with the
    /// <see cref="CsrfProtectedMetadata"/> marker, so P8-01's route inventory test can prove every
    /// mutation is covered from <c>Endpoint.Metadata</c> rather than re-deriving it from the
    /// route prefix (an endpoint filter is invisible in metadata).
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint or group builder.</typeparam>
    /// <param name="builder">The endpoint or group to protect.</param>
    public static TBuilder RequireCsrfHeader<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<TBuilder, CsrfEndpointFilter>();
        builder.WithMetadata(CsrfProtectedMetadata.Instance);
        return builder;
    }
}
