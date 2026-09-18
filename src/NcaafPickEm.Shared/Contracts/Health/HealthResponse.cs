namespace NcaafPickEm.Shared.Contracts.Health;

/// <summary>
/// Body of <c>GET /health</c> and <c>GET /health/ready</c>.
/// </summary>
/// <param name="Status">"ok" when the process is healthy and every readiness check passed.</param>
public sealed record HealthResponse(string Status)
{
    /// <summary>The only healthy value of <see cref="Status"/>.</summary>
    public const string Ok = "ok";

    /// <summary>A healthy response.</summary>
    public static HealthResponse Healthy { get; } = new(Ok);
}
