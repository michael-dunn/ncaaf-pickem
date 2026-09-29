using NcaafPickEm.Simulator.Components;
using NcaafPickEm.Simulator.Engine;

namespace NcaafPickEm.Simulator;

/// <summary>
/// The simulator (P11-01): a separate website that plays the outside world for a Development copy
/// of the app. See Implementation/Phases/Phase-11-Simulator.md and the README's "Simulating a week".
/// </summary>
/// <remarks>
/// An explicit entry point rather than top-level statements: .NET 10 makes a top-level
/// <c>Program</c> public, and the tests reference this project and the app's together, whose
/// <c>Program</c> <c>WebApplicationFactory</c> needs unambiguously.
/// </remarks>
public static class SimulatorProgram
{
    /// <summary>The app's <c>UserSecretsId</c> (NcaafPickEm.Api.csproj).</summary>
    private const string AppUserSecretsId = "ncaaf-pickem-api";

    /// <summary>Runs the simulator's website.</summary>
    public static int Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        if (!builder.Environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                $"NcaafPickEm.Simulator only runs in Development (ASPNETCORE_ENVIRONMENT is '{builder.Environment.EnvironmentName}'). " +
                "It rewrites games, scores and the calendar in its database at will.");
            return 3;
        }

        BorrowTheAppsPushKeys(builder);

        // Last, so nothing in appsettings or the environment can turn an ingest or the app's
        // scheduler on inside this process.
        builder.Configuration.AddInMemoryCollection(SimulatorServiceCollectionExtensions.ForcedSettings);

        builder.Services.AddSimulator(builder.Configuration, builder.Environment);
        builder.Services.AddScoped<DashboardSession>();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        WebApplication app = builder.Build();

        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        app.Run();
        return 0;
    }

    /// <summary>
    /// The reminder jobs and notification handlers the simulator runs push from this process, and a
    /// browser subscription only accepts pushes signed with the VAPID pair it subscribed with - the
    /// app's. So when the simulator has no pair of its own, it borrows the app's from the app's
    /// Development settings file or user secrets, and pushes it sends reach the same phones.
    /// </summary>
    private static void BorrowTheAppsPushKeys(WebApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Push:VapidPublicKey"]))
        {
            return;
        }

        string appSettings = Path.GetFullPath(Path.Combine(
            builder.Environment.ContentRootPath, "..", "..", "src", "NcaafPickEm.Api", "appsettings.Development.json"));

        IConfigurationRoot app = new ConfigurationBuilder()
            .AddJsonFile(appSettings, optional: true)
            .AddUserSecrets(AppUserSecretsId)
            .Build();

        Dictionary<string, string?> push = app.GetSection("Push").AsEnumerable(makePathsRelative: false)
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (push.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(push);
        }
    }
}
