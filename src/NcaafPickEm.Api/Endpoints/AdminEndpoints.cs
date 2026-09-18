using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Operational reporting for commissioners (Features 09, 12).
/// </summary>
/// <remarks>
/// P2-04 extends this group with <c>POST /api/admin/refresh/{dataType}</c> and
/// <c>POST /api/admin/unmatched/{id}/resolve</c>, and fills the counters this endpoint already
/// reports: nothing writes <c>DataRefreshStatus</c>, <c>ProviderCalls</c> or <c>UnmatchedGames</c>
/// until then, so those parts of the response are empty or zero by construction rather than by
/// accident.
/// </remarks>
public static class AdminEndpoints
{
    /// <summary>Calls to CFBD in a month at which the page starts warning about the free tier.</summary>
    public const int CfbdMonthlyCallWarningThreshold = 800;

    /// <summary>How many job runs the data status page lists.</summary>
    public const int RecentJobCount = 50;

    /// <summary>Configuration key naming the live score provider.</summary>
    private const string LiveScoreSourceKey = "Providers:LiveScores";

    /// <summary>Maps <c>GET /api/admin/data-status</c>.</summary>
    /// <param name="builder">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder admin = builder.MapGroup("/admin")
            .WithTags("admin")
            .RequireAnyLeagueCommissioner();

        admin.MapGet("/data-status", GetDataStatusAsync).WithName("AdminDataStatus");

        return builder;
    }

    private static async Task<Ok<DataStatusResponse>> GetDataStatusAsync(
        AppDbContext database,
        IConfiguration configuration,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Dictionary<RefreshDataType, DataRefreshStatus> refreshes = await database.DataRefreshStatuses
            .AsNoTracking()
            .ToDictionaryAsync(status => status.DataType, cancellationToken);

        // One row per slice, always, so the page's shape does not depend on what has run yet.
        RefreshStatusDto[] refreshRows =
        [
            .. Enum.GetValues<RefreshDataType>()
                .Select(dataType => refreshes.TryGetValue(dataType, out DataRefreshStatus? status)
                    ? new RefreshStatusDto(
                        dataType,
                        ToOffset(status.LastSuccessUtc),
                        ToOffset(status.LastAttemptUtc),
                        status.LastError)
                    : new RefreshStatusDto(dataType, null, null, null)),
        ];

        DateTimeOffset now = timeProvider.GetUtcNow();
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime monthEnd = monthStart.AddMonths(1);
        string cfbd = nameof(ProviderSource.Cfbd);

        int cfbdCalls = await database.ProviderCalls
            .AsNoTracking()
            .CountAsync(
                call => call.Provider == cfbd && call.StartedUtc >= monthStart && call.StartedUtc < monthEnd,
                cancellationToken);

        List<UnmatchedGame> unmatched = await database.UnmatchedGames
            .AsNoTracking()
            .Where(game => game.ResolvedUtc == null)
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.FirstSeenUtc)
            .ToListAsync(cancellationToken);

        List<JobRun> recentJobs = await database.JobRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedUtc)
            .Take(RecentJobCount)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new DataStatusResponse(
            refreshRows,
            cfbdCalls,
            cfbdCalls >= CfbdMonthlyCallWarningThreshold,
            configuration[LiveScoreSourceKey] ?? string.Empty,
            [.. unmatched.Select(ToDto)],
            [.. recentJobs.Select(ToDto)]));
    }

    private static UnmatchedGameDto ToDto(UnmatchedGame game) => new(
        game.Id,
        game.Source,
        game.RawHomeName,
        game.RawAwayName,
        game.GameDate,
        ToOffset(game.FirstSeenUtc));

    private static JobRunDto ToDto(JobRun run) => new(
        run.Id,
        run.JobName,
        ToOffset(run.ScheduledForUtc),
        ToOffset(run.StartedUtc),
        ToOffset(run.FinishedUtc),
        run.Success,
        run.Error);

    /// <remarks>
    /// Entity timestamps are UTC <c>DateTime</c> (D-016) and DTO timestamps are
    /// <c>DateTimeOffset</c> (03-API-Contracts.md). The offset is pinned to zero rather than read
    /// off <c>Kind</c>, so a row that somehow arrives Unspecified cannot be re-read as server
    /// local time.
    /// </remarks>
    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is DateTime present ? ToOffset(present) : null;
}
