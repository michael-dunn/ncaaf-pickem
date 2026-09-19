using Microsoft.AspNetCore.DataProtection;

namespace NcaafPickEm.Api.Hosting;

/// <summary>
/// Persists the ASP.NET Core data-protection key ring outside the container (P8-05, D-161).
/// </summary>
/// <remarks>
/// The auth cookie is encrypted with that key ring, and the default key repository for an app
/// with no registered storage is a directory under the process's own home - which in a container
/// is part of the writable layer and is discarded on every <c>docker compose up</c>. Without a
/// mounted key path a redeploy signs the whole family out, which is exactly the thing an
/// unattended watchtower update must not do (D-162). A season-long sliding cookie
/// (<c>AuthDefaults.SessionLifetime</c>) makes that worse, not better.
/// <para>
/// Unset means "leave the framework default alone", so the Windows-service deployment (P8-02),
/// where the key ring already lands in a stable profile directory, is unaffected.
/// </para>
/// </remarks>
public static class DataProtectionSetup
{
    /// <summary>Configuration key holding the directory for the key ring. Unset = framework default.</summary>
    public const string KeysPathKey = "DataProtection:KeysPath";

    /// <summary>
    /// Application discriminator. Fixed rather than derived from the content root so the same key
    /// ring keeps working when the app moves between <c>/app</c> in a container and an install
    /// directory on Windows.
    /// </summary>
    public const string ApplicationName = "NcaafPickEm";

    /// <summary>Points data protection at <see cref="KeysPathKey"/> when it is configured.</summary>
    public static IServiceCollection AddAppDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[KeysPathKey] is not { Length: > 0 } keysPath || string.IsNullOrWhiteSpace(keysPath))
        {
            return services;
        }

        // Idempotent, and deliberately eager: a path the app cannot create is a configuration
        // error worth failing at startup, not one to discover the first time somebody signs in.
        DirectoryInfo directory = Directory.CreateDirectory(keysPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(directory)
            .SetApplicationName(ApplicationName);

        return services;
    }
}
