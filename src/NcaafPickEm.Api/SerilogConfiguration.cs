using Serilog;
using Serilog.Events;

namespace NcaafPickEm.Api;

/// <summary>
/// Serilog wiring: console for interactive runs, a daily rolling file under <c>logs/</c>
/// for the home server. Levels and enrichers come from the <c>Serilog</c> configuration section.
/// </summary>
public static class SerilogConfiguration
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    private const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Configuration key overriding where the rolling file sink writes. Unset means
    /// <c>&lt;content root&gt;/logs</c>, which is what a <c>dotnet run</c> and the Windows service
    /// both want; the container sets it to <c>/app/logs</c>, a mounted volume, so a redeploy does
    /// not take the log history with it (P8-05).
    /// </summary>
    /// <remarks>
    /// It lives under the <c>Serilog</c> section next to <c>MinimumLevel</c>, which
    /// <c>Serilog.Settings.Configuration</c> ignores: that reader only looks at the subsections it
    /// knows (<c>Using</c>, <c>MinimumLevel</c>, <c>WriteTo</c>, <c>Enrich</c>, <c>Filter</c>,
    /// <c>Destructure</c>, <c>Properties</c>) and passes over anything else.
    /// </remarks>
    public const string LogDirectoryKey = "Serilog:LogDirectory";

    /// <summary>
    /// Logger used before the host exists, so startup failures are never silent.
    /// </summary>
    public static Serilog.Core.Logger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .CreateLogger();

    /// <summary>
    /// Real logger for the running host. Sinks are declared here (not in appsettings) so a
    /// missing configuration section can never leave the app without logs; the
    /// <c>Serilog</c> section still controls minimum levels, overrides, and enrichers.
    /// </summary>
    public static void Configure(HostBuilderContext context, LoggerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        string logDirectory = context.Configuration[LogDirectoryKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(context.HostingEnvironment.ContentRootPath, "logs");

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "NcaafPickEm")
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .WriteTo.File(
                Path.Combine(logDirectory, "ncaaf-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 64L * 1024 * 1024,
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: FileTemplate);
    }
}
