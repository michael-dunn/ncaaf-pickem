using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Reads and writes the simulator's own table, <c>sim.Settings</c> (D-186): a key/value table the
/// simulator creates with plain SQL in the database it shares with the app. The app's EF model does
/// not know it exists and its migrations never touch it.
/// </summary>
public sealed class SimStateStore
{
    /// <summary>The key the <see cref="SimState"/> document is stored under.</summary>
    public const string StateKey = "state";

    /// <summary>The key whose presence says "the simulator created this database".</summary>
    public const string MarkerKey = "created";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the store over the scope's database.</summary>
    public SimStateStore(AppDbContext database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <summary>True when <c>sim.Settings</c> exists in this database.</summary>
    public async Task<bool> TableExistsAsync(CancellationToken cancellationToken) =>
        await _database.Database
            .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID(N'sim.Settings', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false) == 1;

    /// <summary>Creates the <c>sim</c> schema and <c>sim.Settings</c> if missing, and marks the database as the simulator's.</summary>
    public async Task CreateTableAsync(CancellationToken cancellationToken)
    {
        await _database.Database.ExecuteSqlRawAsync(
            """
            IF SCHEMA_ID(N'sim') IS NULL EXEC(N'CREATE SCHEMA [sim]');
            IF OBJECT_ID(N'sim.Settings', N'U') IS NULL
                CREATE TABLE [sim].[Settings] (
                    [Key] nvarchar(100) NOT NULL CONSTRAINT [PK_sim_Settings] PRIMARY KEY,
                    [Value] nvarchar(max) NOT NULL,
                    [UpdatedUtc] datetime2 NOT NULL);
            """,
            cancellationToken).ConfigureAwait(false);

        await WriteAsync(MarkerKey, _timeProvider.GetUtcNow().ToString("o", CultureInfo.InvariantCulture), onlyIfMissing: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The stored state, or a fresh one for <paramref name="defaultSeasonYear"/>.</summary>
    /// <param name="defaultSeasonYear">The season a brand-new state simulates.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<SimState> LoadAsync(int defaultSeasonYear, CancellationToken cancellationToken)
    {
        string? json = await ReadAsync(StateKey, cancellationToken).ConfigureAwait(false);
        SimState? state = json is null ? null : JsonSerializer.Deserialize<SimState>(json, JsonOptions);

        state ??= new SimState();
        if (state.SeasonYear == 0)
        {
            state.SeasonYear = defaultSeasonYear;
        }

        state.Slate ??= new SlateOptions();
        state.Plans ??= [];
        return state;
    }

    /// <summary>Writes the state back.</summary>
    public Task SaveAsync(SimState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WriteAsync(StateKey, JsonSerializer.Serialize(state, JsonOptions), onlyIfMissing: false, cancellationToken);
    }

    private async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        List<string> values = await _database.Database
            .SqlQuery<string>($"SELECT [Value] AS [Value] FROM [sim].[Settings] WHERE [Key] = {key}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return values.Count == 0 ? null : values[0];
    }

    private async Task WriteAsync(string key, string value, bool onlyIfMissing, CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (onlyIfMissing)
        {
            await _database.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (SELECT 1 FROM [sim].[Settings] WHERE [Key] = {key})
                    INSERT INTO [sim].[Settings] ([Key], [Value], [UpdatedUtc]) VALUES ({key}, {value}, {nowUtc});
                """,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await _database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [sim].[Settings] SET [Value] = {value}, [UpdatedUtc] = {nowUtc} WHERE [Key] = {key};
            IF @@ROWCOUNT = 0
                INSERT INTO [sim].[Settings] ([Key], [Value], [UpdatedUtc]) VALUES ({key}, {value}, {nowUtc});
            """,
            cancellationToken).ConfigureAwait(false);
    }
}
