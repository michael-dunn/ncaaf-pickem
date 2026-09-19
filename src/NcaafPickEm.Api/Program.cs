using NcaafPickEm.Api;
using NcaafPickEm.Api.Endpoints;
using NcaafPickEm.Infrastructure;
using NcaafPickEm.Infrastructure.Push;
using Serilog;

// Hidden maintenance command (P7-01): print a fresh VAPID key pair and exit without touching
// configuration, the database, or the network. deploy/generate-vapid.ps1 wraps this.
if (args is [VapidKeyGenerator.CommandName, ..])
{
    VapidKeyGenerator.WriteNewKeyPair(Console.Out);
    return;
}

// Bootstrap logger: captures failures that happen before the host is built.
Log.Logger = SerilogConfiguration.CreateBootstrapLogger();

try
{
    Log.Information("Starting NcaafPickEm.Api");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // P8-02: registers proper Windows Service Control Manager integration (start pending/running
    // status, graceful stop) when launched by the SCM (deploy/install-service.ps1). A no-op
    // everywhere else (dotnet run, tests, Docker) - it detects the hosting context itself.
    builder.Host.UseWindowsService();

    builder.Host.UseSerilog(SerilogConfiguration.Configure);

    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.AddApiServices(builder.Configuration);

    WebApplication app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // Called explicitly, and deliberately after UseStatusCodePages: WebApplication would otherwise
    // insert both before any user middleware, and a 401 raised out there would come back with an
    // empty body instead of ProblemDetails JSON. Routing is still auto-inserted first, so the
    // authorization middleware sees the endpoint's metadata.
    app.UseAuthentication();
    app.UseAuthorization();

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
