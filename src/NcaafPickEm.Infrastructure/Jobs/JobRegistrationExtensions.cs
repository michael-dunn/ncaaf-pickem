using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// How the scheduler and the jobs that run on it are registered.
/// </summary>
/// <remarks>
/// A later phase adding a job touches nothing but its own file and one
/// <c>AddScheduledJob&lt;T&gt;()</c> or <c>AddOneShotJob&lt;T&gt;()</c> line in
/// <c>Infrastructure/DependencyInjection.cs</c>.
/// </remarks>
public static class JobRegistrationExtensions
{
    /// <summary>
    /// Registers the scheduler itself, binds the <c>Jobs</c> configuration section, and adds
    /// <see cref="HeartbeatJob"/>.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration, for the <c>Jobs</c> section.</param>
    /// <remarks>
    /// The hosted service is registered whatever <c>Jobs:Enabled</c> says; it reads the flag when
    /// it starts and returns straight away when it is false, so the registration graph is the
    /// same shape in tests as in production.
    /// </remarks>
    public static IServiceCollection AddJobScheduler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.SectionName));
        services.TryAddSingleton<SchedulerTick>();
        services.AddHostedService<JobScheduler>();

        services.AddScheduledJob<HeartbeatJob>();

        return services;
    }

    /// <summary>Registers a cron job. Scoped, so it may take <c>AppDbContext</c>.</summary>
    /// <typeparam name="TJob">The job.</typeparam>
    /// <param name="services">The container.</param>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services)
        where TJob : class, IScheduledJob
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IScheduledJob, TJob>();
        return services;
    }

    /// <summary>Registers a database-scheduled one-shot job. Scoped, like a cron job.</summary>
    /// <typeparam name="TJob">The job.</typeparam>
    /// <param name="services">The container.</param>
    public static IServiceCollection AddOneShotJob<TJob>(this IServiceCollection services)
        where TJob : class, IOneShotJob
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOneShotJob, TJob>();
        return services;
    }
}
