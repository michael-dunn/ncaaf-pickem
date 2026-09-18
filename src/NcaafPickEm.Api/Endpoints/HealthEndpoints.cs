using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Shared.Contracts.Health;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Liveness and readiness probes. Mapped at the root (not under <c>/api</c>) so they
/// never require authentication or the CSRF header.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Maps <c>/health</c> and <c>/health/ready</c>.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/health", GetLive)
            .WithName("HealthLive")
            .WithTags("health")
            .AllowAnonymous();

        builder.MapGet("/health/ready", GetReady)
            .WithName("HealthReady")
            .WithTags("health")
            .AllowAnonymous();

        return builder;
    }

    /// <summary>
    /// Liveness: the process started and the pipeline responds. Never touches a dependency.
    /// </summary>
    private static Ok<HealthResponse> GetLive() => TypedResults.Ok(HealthResponse.Healthy);

    /// <summary>
    /// Readiness: the app can serve traffic.
    /// </summary>
    /// <remarks>
    /// P0-02 replaces this body with a database round-trip (and widens the return type to
    /// <c>Results&lt;Ok&lt;HealthResponse&gt;, ProblemHttpResult&gt;</c> so an unreachable
    /// database yields 503).
    /// </remarks>
    private static Ok<HealthResponse> GetReady() => TypedResults.Ok(HealthResponse.Healthy);
}
