using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// Web push registration: one <c>services.AddPush(configuration)</c> line in
/// <c>Infrastructure/DependencyInjection.cs</c>.
/// </summary>
public static class PushRegistrationExtensions
{
    /// <summary>
    /// Binds <c>Push:*</c>, picks the transport, and registers the notification service and the
    /// retry job.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <remarks>
    /// The transport is chosen at registration time from whether the configured VAPID pair
    /// actually validates. Missing or placeholder keys are not a startup failure — the app boots
    /// with <see cref="NullPushSender"/>, <c>GET /api/push/vapid-public-key</c> answers 503, and
    /// every send is logged as Failed with the reason.
    /// </remarks>
    public static IServiceCollection AddPush(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));

        var options = new PushOptions();
        configuration.GetSection(PushOptions.SectionName).Bind(options);
        VapidConfiguration vapid = VapidConfiguration.FromOptions(options);

        services.TryAddSingleton(vapid);

        if (vapid.IsConfigured)
        {
            services.AddHttpClient(WebPushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
            services.TryAddSingleton<IPushSender, WebPushSender>();
        }
        else
        {
            services.TryAddSingleton<IPushSender, NullPushSender>();
        }

        services.TryAddScoped<NotificationService>();
        services.AddOneShotJob<PushRetryJob>();

        return services;
    }
}
