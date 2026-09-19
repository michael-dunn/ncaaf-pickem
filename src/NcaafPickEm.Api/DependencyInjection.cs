using FluentValidation;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Points;

using NcaafPickEm.Shared.Contracts.Push;

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
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddProblemDetails();

        // Cookie scheme + Google handler + the three policies (Feature 08, Option A).
        services.AddAppAuthentication(configuration);

        // FluentValidation validators, run by ValidationFilter<TRequest> on the endpoints that
        // need them (05-Conventions.md). Registered by hand rather than assembly scanning so the
        // list here is the list of validators that actually exist.
        services.AddScoped<IValidator<CreateLeagueRequest>, CreateLeagueRequestValidator>();
        services.AddScoped<IValidator<UpdateLeagueSettingsRequest>, UpdateLeagueSettingsRequestValidator>();
        services.AddScoped<IValidator<SetLeagueDisplayNameRequest>, SetLeagueDisplayNameRequestValidator>();
        services.AddScoped<IValidator<TransferRequest>, TransferRequestValidator>();
        services.AddScoped<IValidator<AddGameRequest>, AddGameRequestValidator>();
        services.AddScoped<IValidator<SetPointOverrideRequest>, SetPointOverrideRequestValidator>();
        services.AddScoped<IValidator<ResolveUnmatchedRequest>, ResolveUnmatchedRequestValidator>();

        services.AddScoped<IValidator<PushSubscriptionRequest>, PushSubscriptionRequestValidator>();
        services.AddScoped<IValidator<DeletePushSubscriptionRequest>, DeletePushSubscriptionRequestValidator>();

        return services;
    }
}
