using NcaafPickEm.Api;
using NcaafPickEm.Api.Endpoints;
using NcaafPickEm.Infrastructure;
using Serilog;

// Bootstrap logger: captures failures that happen before the host is built.
Log.Logger = SerilogConfiguration.CreateBootstrapLogger();

try
{
    Log.Information("Starting NcaafPickEm.Api");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog(SerilogConfiguration.Configure);

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiServices();

    WebApplication app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // Hosting of the Blazor WebAssembly PWA (NcaafPickEm.Web) as one deployable unit.
    // MapStaticAssets serves the Web project's wwwroot *and* its _framework payload from the
    // static-asset endpoint manifest, with fingerprinting and Brotli/gzip negotiation built in.
    // Do not add UseBlazorFrameworkFiles(): its private static-file branch bypasses the endpoint
    // middleware and 500s on every /_framework request once MapStaticAssets owns those routes.
    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }

    app.MapStaticAssets();

    app.MapApiEndpoints();

    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "NcaafPickEm.Api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the app in API tests.
/// </summary>
public partial class Program;
