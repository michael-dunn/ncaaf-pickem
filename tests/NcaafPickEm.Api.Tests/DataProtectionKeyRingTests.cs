using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Hosting;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-05 / D-161: <c>DataProtection__KeysPath</c> keeps the auth cookie readable across restarts.
/// </summary>
/// <remarks>
/// Two separately built hosts stand in for "the container before and after
/// <c>docker compose pull &amp;&amp; up -d</c>". Sharing a key path, the second must be able to
/// unprotect what the first protected — otherwise every unattended watchtower update signs the
/// whole family out of a season-long cookie.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class DataProtectionKeyRingTests : IDisposable
{
    private const string Purpose = "NcaafPickEm.Tests.Cookie";

    private readonly ApiTestFixture _fixture;
    private readonly string _keysPath;
    private readonly string _otherKeysPath;

    public DataProtectionKeyRingTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _keysPath = Path.Combine(Path.GetTempPath(), $"ncaaf-keys-{Guid.CreateVersion7():N}");
        _otherKeysPath = Path.Combine(Path.GetTempPath(), $"ncaaf-keys-{Guid.CreateVersion7():N}");
    }

    public void Dispose()
    {
        Delete(_keysPath);
        Delete(_otherKeysPath);
    }

    [Fact]
    public async Task GivenASharedKeysPath_WhenTheAppRestarts_ThenTheOldPayloadStillUnprotects()
    {
        string payload;

        await using (KeyedFactory first = new(_fixture.Database.ConnectionString, _keysPath))
        {
            payload = Protector(first).Protect("michael");
        }

        // The key ring outlived the host that wrote it.
        Directory.EnumerateFiles(_keysPath, "key-*.xml").Should().NotBeEmpty();

        await using KeyedFactory second = new(_fixture.Database.ConnectionString, _keysPath);

        Protector(second).Unprotect(payload).Should().Be("michael");
    }

    [Fact]
    public async Task GivenADifferentKeysPath_WhenUnprotecting_ThenItFails()
    {
        // The negative half: without the shared path the payload really is unreadable, so the
        // test above is proving the mount and not some ambient machine-wide key store.
        await using KeyedFactory first = new(_fixture.Database.ConnectionString, _keysPath);
        await using KeyedFactory second = new(_fixture.Database.ConnectionString, _otherKeysPath);

        string payload = Protector(first).Protect("michael");

        Action unprotect = () => Protector(second).Unprotect(payload);

        unprotect.Should().Throw<CryptographicException>();
    }

    private static IDataProtector Protector(KeyedFactory factory) =>
        factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>The app with its data-protection key ring pointed at a throwaway directory.</summary>
    private sealed class KeyedFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _keysPath;

        public KeyedFactory(string connectionString, string keysPath)
        {
            _connectionString = connectionString;
            _keysPath = keysPath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jobs:Enabled", "false");
            builder.UseSetting("Providers:ReferenceData", "Fixture");
            builder.UseSetting("Providers:LiveScores", "Fixture");
            builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");
            builder.UseSetting(DataProtectionSetup.KeysPathKey, _keysPath);
        }
    }
}
