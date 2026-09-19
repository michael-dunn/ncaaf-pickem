using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Simulation;

/// <summary>
/// The hidden <c>simulate</c> argument (P8-03): drives one fixture week by hand so the operator
/// can dry-run a whole Saturday on the home server before the real season starts.
/// </summary>
/// <remarks>
/// <para>
/// <c>dotnet run --project src/NcaafPickEm.Api -- simulate --week 7 --snapshot 3 [--lock]
/// [--tick "2026-10-16T20:00:00-04:00"] [--league "Family League"]</c>
/// </para>
/// <para>
/// Development only, and only against <c>Providers:ReferenceData=Fixture</c> - it advances
/// <see cref="FixtureSnapshotState"/> and writes scores onto the fixture games, which would be
/// nonsense against a real provider. It builds the app's own container (nothing is duplicated:
/// the same <c>AddInfrastructure</c>, the same services and jobs the server runs) but never
/// starts Kestrel or the hosted services, so migrating and seeding are done explicitly here in a
/// known order.
/// </para>
/// <para>
/// <c>--tick</c> pins <see cref="TimeProvider"/> for the whole run, so a tick at Friday 20:00 ET
/// fires the Friday reminder exactly as the scheduler would, and the generate/lock steps in the
/// same invocation see the same instant.
/// </para>
/// </remarks>
public static class SimulateCommand
{
    /// <summary>The command-line argument that runs a simulation step instead of starting the app.</summary>
    public const string CommandName = "simulate";

    /// <summary>Parses <paramref name="args"/>, runs the requested steps, and prints a summary.</summary>
    /// <param name="args">The process arguments, starting with <see cref="CommandName"/>.</param>
    /// <param name="output">Where the summary goes; defaults to <see cref="Console.Out"/>.</param>
    /// <returns>Zero on success, non-zero when the arguments or the environment are wrong.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        TextWriter write = output ?? Console.Out;

        SimulateOptions options;
        try
        {
            options = SimulateOptions.Parse(args);
        }
        catch (FormatException exception)
        {
            write.WriteLine(exception.Message);
            write.WriteLine(SimulateOptions.Usage);
            return 2;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
        });

        if (!builder.Environment.IsDevelopment())
        {
            write.WriteLine(
                $"simulate is a Development-only tool; the environment is '{builder.Environment.EnvironmentName}'.");
            return 3;
        }

        // The demo league is the thing being simulated, and the scheduler must not tick behind
        // this process's back while it does one step at a time.
        builder.Configuration["Seed:DemoLeague"] = "true";
        builder.Configuration["Jobs:Enabled"] = "false";

        // --tick means "run what is due at exactly this minute". The production hour-wide
        // catch-up window would otherwise replay a dozen heartbeats and whatever refresh jobs
        // happen to sit just before the instant the operator named.
        builder.Configuration["Jobs:CatchUpMinutes"] = "1";

        // The summary is the output of this tool; EF's statement log would bury it. Jobs, the
        // seeder and the poller still log at Information, which is what the operator wants to see.
        builder.Configuration["Logging:LogLevel:Microsoft"] = "Warning";
        builder.Configuration["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning";

        if (options.TickAt is DateTimeOffset tickAt)
        {
            // Registered before AddInfrastructure, whose TryAddSingleton(TimeProvider.System)
            // then no-ops - the same hook ApiFactory uses in the tests.
            builder.Services.AddSingleton<TimeProvider>(new PinnedTimeProvider(tickAt));
        }

        builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

        await using WebApplication app = builder.Build();

        string referenceData = builder.Configuration["Providers:ReferenceData"] ?? string.Empty;
        if (!string.Equals(referenceData, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            write.WriteLine(
                $"simulate needs Providers:ReferenceData=Fixture; it is '{referenceData}'. Nothing was changed.");
            return 4;
        }

        await using AsyncServiceScope scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        AppDbContext database = services.GetRequiredService<AppDbContext>();
        TimeProvider clock = services.GetRequiredService<TimeProvider>();

        await database.Database.MigrateAsync();
        await services.GetRequiredService<FixtureSeeder>().SeedAsync(seedDemoLeague: true);

        League? league = await database.Leagues
            .FirstOrDefaultAsync(candidate => candidate.Name == options.LeagueName);

        if (league is null)
        {
            write.WriteLine($"No league named '{options.LeagueName}'. Nothing was changed.");
            return 5;
        }

        IReadOnlyList<SeasonWeek> weeks = await services.GetRequiredService<ISeasonWeekSource>()
            .GetWeeksAsync(league.SeasonYear);

        int week = options.Week ?? SeasonCalendar.CurrentWeekAt(clock.GetUtcNow(), weeks).Week;
        week = Math.Clamp(week, league.FirstWeek, league.LastWeek);

        write.WriteLine($"Simulating league '{league.Name}' ({league.SeasonYear}) week {week}");
        write.WriteLine($"  now: {clock.GetUtcNow():o}{(options.TickAt is null ? string.Empty : " (pinned)")}");

        await EnsureDefaultRulesAsync(services, database, league, write);
        await GenerateAsync(services, league, week, write);

        if (options.Lock)
        {
            await LockAsync(services, database, league, week, write);
        }

        if (options.Snapshot is int snapshot)
        {
            await PollAsync(services, database, clock, weeks, week, snapshot, write);
        }

        if (options.TickAt is DateTimeOffset instant)
        {
            write.WriteLine($"  tick: running the scheduler at {instant:o}");
            await app.Services.GetRequiredService<SchedulerTick>().TickAsync(instant, CancellationToken.None);
        }

        await SummariseAsync(database, league, week, write);
        return 0;
    }

    private static async Task EnsureDefaultRulesAsync(
        IServiceProvider services,
        AppDbContext database,
        League league,
        TextWriter write)
    {
        bool hasRules = await database.GameSetRules.AnyAsync(rule => rule.LeagueId == league.Id && rule.Week == null);
        if (hasRules)
        {
            return;
        }

        GameSetRuleDto[] rules = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
        await services.GetRequiredService<GameSetService>().ReplaceDefaultRulesAsync(league.Id, rules, CancellationToken.None);
        write.WriteLine("  rules: seeded the default [Top 25] rule (the league had none)");
    }

    private static async Task GenerateAsync(IServiceProvider services, League league, int week, TextWriter write)
    {
        GameSetService gameSets = services.GetRequiredService<GameSetService>();

        try
        {
            await gameSets.GetOrCreateWeekSetAsync(league.Id, week, CancellationToken.None);
            WeekGameSetResponse response = await gameSets.GenerateAsync(league.Id, week, CancellationToken.None);
            write.WriteLine($"  generate: {response.Games.Length} game(s) in the set");
        }
        catch (GameSetRuleViolation violation)
        {
            write.WriteLine($"  generate: skipped ({violation.Code})");
        }
    }

    /// <summary>
    /// Runs <see cref="LockWeekJob"/> against this week's set whatever the clock says, which is
    /// the point of the switch: the operator is asking for the lock to happen now, not waiting
    /// for <c>LockAtUtc</c> to arrive.
    /// </summary>
    private static async Task LockAsync(
        IServiceProvider services,
        AppDbContext database,
        League league,
        int week,
        TextWriter write)
    {
        WeekGameSet? set = await database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.LeagueId == league.Id && candidate.Week == week);

        if (set is null || set.LockAtUtc is null)
        {
            write.WriteLine("  lock: nothing to lock (no set, or the set has no active games)");
            return;
        }

        LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(services);
        await job.RunAsync(
            new OneShotOccurrence(
                set.Id.ToString("N"),
                new DateTimeOffset(DateTime.SpecifyKind(set.LockAtUtc.Value, DateTimeKind.Utc))),
            CancellationToken.None);

        write.WriteLine("  lock: LockWeekJob ran");
    }

    private static async Task PollAsync(
        IServiceProvider services,
        AppDbContext database,
        TimeProvider clock,
        IReadOnlyList<SeasonWeek> weeks,
        int week,
        int snapshot,
        TextWriter write)
    {
        services.GetRequiredService<FixtureSnapshotState>().Set(snapshot);

        SeasonWeek? seasonWeek = weeks.FirstOrDefault(candidate => candidate.Week == week);
        DateOnly saturday = seasonWeek is null
            ? DateOnly.FromDateTime(SeasonCalendar.ToEastern(clock.GetUtcNow()).DateTime)
            : DateOnly.FromDateTime(SeasonCalendar.ToEastern(seasonWeek.EndUtc).DateTime);

        LiveScoreApplyResult result = await SaturdayPoller.PollOnceAsync(
            services,
            database,
            clock,
            saturday,
            services.GetRequiredService<ILogger<SaturdayPoller>>(),
            CancellationToken.None);

        write.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  snapshot {snapshot} polled for {saturday:yyyy-MM-dd} (Eastern): "
                + $"{result.Matched} matched, {result.Changed} changed, {result.Unmatched} unmatched"));
    }

    private static async Task SummariseAsync(AppDbContext database, League league, int week, TextWriter write)
    {
        WeekGameSet? set = await database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.LeagueId == league.Id && candidate.Week == week);

        if (set is null)
        {
            write.WriteLine("  summary: the week has no game set");
            return;
        }

        write.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  set: LockAtUtc={Format(set.LockAtUtc)} LockedUtc={Format(set.LockedUtc)} IsComplete={set.IsComplete}"));

        var games = await database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == set.Id)
            .Select(row => new
            {
                Home = row.Game!.HomeTeam!.School,
                Away = row.Game!.AwayTeam!.School,
                row.Game!.Status,
                row.Game!.HomeScore,
                row.Game!.AwayScore,
                row.ResolvedPointValue,
                row.IsRemoved,
                row.IsVoided,
            })
            .OrderBy(row => row.Away)
            .ToListAsync();

        foreach (var game in games)
        {
            string flags = game.IsVoided ? " [voided]" : game.IsRemoved ? " [removed]" : string.Empty;
            write.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {game.Away} {game.AwayScore?.ToString(CultureInfo.InvariantCulture) ?? "-"} "
                    + $"@ {game.Home} {game.HomeScore?.ToString(CultureInfo.InvariantCulture) ?? "-"} "
                    + $"({game.Status}, {game.ResolvedPointValue} pts){flags}"));
        }

        var members = await database.Memberships
            .AsNoTracking()
            .Where(membership => membership.LeagueId == league.Id && membership.RemovedUtc == null)
            .Select(membership => new
            {
                // MemberNameProjection.Selector inlined: it cannot be composed into an anonymous
                // projection, and ReminderRecipients inlines the same rule for the same reason.
                Name = membership.DisplayNameOverride ?? (membership.User != null ? membership.User.DisplayName : string.Empty),
                Status = database.WeekSubmissions
                    .Where(row => row.MembershipId == membership.Id && row.WeekGameSetId == set.Id)
                    .Select(row => (SubmissionStatus?)row.Status)
                    .FirstOrDefault(),
                Points = database.WeekResults
                    .Where(row => row.MembershipId == membership.Id && row.WeekGameSetId == set.Id)
                    .Select(row => (int?)row.Points)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        foreach (var member in members.OrderByDescending(member => member.Points ?? 0).ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase))
        {
            write.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {member.Name}: {member.Status?.ToString() ?? "NotStarted"}, {member.Points ?? 0} pts"));
        }
    }

    private static string Format(DateTime? instant) =>
        instant is null ? "-" : instant.Value.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>A clock stuck at the instant <c>--tick</c> named.</summary>
    private sealed class PinnedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
