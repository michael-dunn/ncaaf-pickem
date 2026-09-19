using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Infrastructure.Data;
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

        builder.MapGet("/health/ready", GetReadyAsync)
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
    /// Readiness: the app can serve traffic, which means it can reach SQL Server.
    /// Returns 503 with <c>ProblemDetails</c> when the database is unreachable or unconfigured,
    /// or while startup migration is still running (P8-05 — Kestrel is already listening by then,
    /// so "reachable" and "ready" are not the same answer during a container's first boot).
    /// </summary>
    private static async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> GetReadyAsync(
        AppDbContext database,
        DatabaseStartupState startupState,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (startupState.IsMigrating)
        {
            return TypedResults.Problem(
                title: "Database migration in progress",
                detail: "The API is applying schema migrations and is not ready to serve traffic.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            if (await database.Database.CanConnectAsync(cancellationToken))
            {
                return TypedResults.Ok(HealthResponse.Healthy);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
        {
            // An unconfigured connection string throws InvalidOperationException rather than
            // returning false, and a dead server can surface as either.
            loggerFactory
                .CreateLogger(typeof(HealthEndpoints))
                .LogWarning(ex, "Readiness probe could not reach the database");
        }

        return TypedResults.Problem(
            title: "Database unavailable",
            detail: "The API cannot reach SQL Server.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
