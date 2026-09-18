using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api;

/// <summary>
/// Single registration point for API-layer services (auth, validation, endpoint filters, JSON options).
/// Program.cs calls this once; later phases add registrations here, not in Program.cs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the HTTP layer needs.
    /// </summary>
    /// <remarks>
    /// Later phases add:
    /// <list type="bullet">
    ///   <item>P1-01 onwards — FluentValidation validators from <c>Api/Validation</c>.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddProblemDetails();

        // Cookie scheme + Google handler + the three policies (Feature 08, Option A).
        services.AddAppAuthentication(configuration);

        return services;
    }
}
