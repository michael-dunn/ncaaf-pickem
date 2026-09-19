using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs.Refresh;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Operational reporting for commissioners (Features 09, 12), plus the manual refresh and
/// unmatched-game resolution actions P2-04 adds.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Calls to CFBD in a month at which the page starts warning about the free tier.</summary>
    public const int CfbdMonthlyCallWarningThreshold = 800;

    /// <summary>How many job runs the data status page lists.</summary>
    public const int RecentJobCount = 50;

    /// <summary>Configuration key naming the live score provider.</summary>
    private const string LiveScoreSourceKey = "Providers:LiveScores";

    /// <summary>
    /// Must match the options <c>LiveScoreApplyService</c> wrote <c>UnmatchedGames.RawPayload</c>
    /// with: web defaults are camel-cased, and reading them back with the case-sensitive default
    /// options would silently bind nothing and never learn the ESPN event id.
    /// </summary>
    private static readonly JsonSerializerOptions RawPayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps every <c>/api/admin/*</c> route.</summary>
    /// <param name="builder">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder admin = builder.MapGroup("/admin")
            .WithTags("admin")
            .RequireAnyLeagueCommissioner();

        admin.MapGet("/data-status", GetDataStatusAsync).WithName("AdminDataStatus");
        admin.MapPost("/refresh/{dataType}", RefreshAsync).WithName("AdminManualRefresh");
        admin.MapPost("/unmatched/{id:guid}/resolve", ResolveUnmatchedAsync)
            .WithName("AdminResolveUnmatched")
            .AddEndpointFilter<ValidationFilter<ResolveUnmatchedRequest>>();

        return builder;
    }

    private static async Task<Ok<DataStatusResponse>> GetDataStatusAsync(
        AppDbContext database,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILiveScoreHealth liveScoreHealth,
        GameSetService gameSetService,
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

        List<NeedsReviewGameDto> needsReview = await GetNeedsReviewAsync(database, cancellationToken);

        // P3-04: a game postponed or cancelled *after* its week locked also needs a commissioner
        // decision (void it, or override the result) and belongs on the same list - that is what
        // GameNeedsVoidReview is raised for. Feature 02's card asks for the data page to list it.
        needsReview.AddRange((await gameSetService.ListNeedsVoidReviewAsync(null, cancellationToken))
            .Select(item => new NeedsReviewGameDto(
                item.GameId,
                item.LeagueId,
                item.LeagueName,
                item.Week,
                item.HomeTeam,
                item.AwayTeam,
                null,
                null,
                item.Status.ToString())));

        return TypedResults.Ok(new DataStatusResponse(
            refreshRows,
            cfbdCalls,
            cfbdCalls >= CfbdMonthlyCallWarningThreshold,
            configuration[LiveScoreSourceKey] ?? string.Empty,
            [.. unmatched.Select(ToDto)],
            [.. recentJobs.Select(ToDto)],
            liveScoreHealth.ActiveSource.ToString(),
            liveScoreHealth.ScoresMayBeStale,
            needsReview));
    }

    private static async Task<List<NeedsReviewGameDto>> GetNeedsReviewAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        var rows = await database.WeekGameSetGames
            .AsNoTracking()
            .Where(setGame => !setGame.IsRemoved
                && !setGame.IsVoided
                && setGame.ResultOverrideWinnerTeamId == null
                && setGame.Game!.Status == GameStatus.Final
                && (setGame.Game.HomeScore == null
                    || setGame.Game.AwayScore == null
                    || setGame.Game.HomeScore == setGame.Game.AwayScore))
            .Select(setGame => new
            {
                setGame.GameId,
                setGame.WeekGameSet!.LeagueId,
                LeagueName = setGame.WeekGameSet.League!.Name,
                setGame.WeekGameSet.Week,
                HomeTeam = setGame.Game!.HomeTeam!.School,
                AwayTeam = setGame.Game.AwayTeam!.School,
                setGame.Game.HomeScore,
                setGame.Game.AwayScore,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new NeedsReviewGameDto(
            row.GameId,
            row.LeagueId,
            row.LeagueName,
            row.Week,
            row.HomeTeam,
            row.AwayTeam,
            row.HomeScore,
            row.AwayScore,
            row.HomeScore is null || row.AwayScore is null ? "Missing score" : "Tie"))];
    }

    private static async Task<Results<Accepted<ManualRefreshResponse>, ProblemHttpResult>> RefreshAsync(
        string dataType,
        HttpContext httpContext,
        AppDbContext database,
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        ILiveScoreProvider liveScoreProvider,
        LiveScoreApplyService liveScoreApplyService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(dataType, ignoreCase: true, out RefreshDataType parsed)
            || !Enum.IsDefined(parsed))
        {
            return TypedResults.Problem(
                title: "Unknown data type",
                detail: $"'{dataType}' is not a refreshable slice.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        int season = RefreshJobSupport.CurrentSeasonYear(nowUtc);

        ManualRefreshResponse result = parsed switch
        {
            RefreshDataType.Teams => ToResponse(parsed, await ingest.IngestTeamsAsync(season, cancellationToken)),
            RefreshDataType.Schedule => await RefreshScheduleAsync(ingest, weekSource, season, nowUtc, cancellationToken),
            RefreshDataType.Rankings => await RefreshRankingsAsync(ingest, weekSource, season, nowUtc, cancellationToken),
            RefreshDataType.Lines => await RefreshLinesAsync(ingest, weekSource, season, nowUtc, cancellationToken),
            RefreshDataType.Scores => await RefreshScoresNowAsync(
                liveScoreProvider, liveScoreApplyService, database, timeProvider, cancellationToken),
            _ => new ManualRefreshResponse(parsed, false, "Not implemented"),
        };

        await WriteManualRefreshAuditAsync(database, httpContext, parsed, timeProvider, cancellationToken);

        return TypedResults.Accepted((string?)null, result);
    }

    private static ManualRefreshResponse ToResponse(RefreshDataType dataType, TeamsIngestResult result) =>
        new(dataType, result.Success, result.Error);

    private static async Task<ManualRefreshResponse> RefreshScheduleAsync(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        int season,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        int? week = await RefreshJobSupport.CurrentWeekAsync(weekSource, season, nowUtc, cancellationToken);
        if (week is not int currentWeek)
        {
            return new ManualRefreshResponse(RefreshDataType.Schedule, false, "No calendar on file for this season yet");
        }

        ScheduleIngestResult result = await ingest.IngestScheduleAsync(season, currentWeek, cancellationToken);
        return new ManualRefreshResponse(RefreshDataType.Schedule, result.Success, result.Error);
    }

    private static async Task<ManualRefreshResponse> RefreshRankingsAsync(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        int season,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        int? week = await RefreshJobSupport.CurrentWeekAsync(weekSource, season, nowUtc, cancellationToken);
        if (week is not int currentWeek)
        {
            return new ManualRefreshResponse(RefreshDataType.Rankings, false, "No calendar on file for this season yet");
        }

        RankingsIngestResult result = await ingest.IngestRankingsAsync(season, currentWeek, cancellationToken);
        return new ManualRefreshResponse(RefreshDataType.Rankings, result.Success, result.Error);
    }

    private static async Task<ManualRefreshResponse> RefreshLinesAsync(
        ReferenceDataIngestService ingest,
        ISeasonWeekSource weekSource,
        int season,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        int? week = await RefreshJobSupport.CurrentWeekAsync(weekSource, season, nowUtc, cancellationToken);
        if (week is not int currentWeek)
        {
            return new ManualRefreshResponse(RefreshDataType.Lines, false, "No calendar on file for this season yet");
        }

        LinesIngestResult result = await ingest.IngestLinesAsync(season, currentWeek, cancellationToken);
        return new ManualRefreshResponse(RefreshDataType.Lines, result.Success, result.Error);
    }

    private static async Task<ManualRefreshResponse> RefreshScoresNowAsync(
        ILiveScoreProvider liveScoreProvider,
        LiveScoreApplyService liveScoreApplyService,
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateOnly easternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(timeProvider.GetUtcNow()).Date);

        try
        {
            IReadOnlyList<LiveScoreUpdate> updates =
                await liveScoreProvider.GetScoresAsync(easternDate, cancellationToken);
            await liveScoreApplyService.ApplyAsync(easternDate, updates, cancellationToken);

            DataRefreshStatus? status = await database.DataRefreshStatuses
                .FirstOrDefaultAsync(row => row.DataType == RefreshDataType.Scores, cancellationToken);
            if (status is null)
            {
                status = new DataRefreshStatus { DataType = RefreshDataType.Scores };
                database.DataRefreshStatuses.Add(status);
            }

            DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            status.LastAttemptUtc = nowUtc;
            status.LastSuccessUtc = nowUtc;
            status.LastError = null;
            await database.SaveChangesAsync(cancellationToken);

            return new ManualRefreshResponse(RefreshDataType.Scores, true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ManualRefreshResponse(RefreshDataType.Scores, false, ex.Message);
        }
    }

    private static async Task WriteManualRefreshAuditAsync(
        AppDbContext database,
        HttpContext httpContext,
        RefreshDataType dataType,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Guid userId = httpContext.RequestServices.GetRequiredService<ICurrentUser>().UserId;

        // /api/admin/* is not scoped to a {leagueId}, but AuditLog requires one; the caller's
        // first commissioner league (by JoinedUtc) stands in, since RequireAnyLeagueCommissioner
        // already guarantees at least one exists (see 03-API-Contracts.md).
        Membership? membership = await database.Memberships
            .Where(member => member.UserId == userId
                && member.RemovedUtc == null
                && member.Role == MembershipRole.Commissioner)
            .OrderBy(member => member.JoinedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return;
        }

        database.AuditLog.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            LeagueId = membership.LeagueId,
            ActorMembershipId = membership.Id,
            Action = AuditAction.ManualRefresh,
            TargetId = null,
            Details = JsonSerializer.Serialize(new { DataType = dataType.ToString() }),
            CreatedUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ResolveUnmatchedAsync(
        Guid id,
        ResolveUnmatchedRequest request,
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        UnmatchedGame? unmatched = await database.UnmatchedGames
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (unmatched is null)
        {
            return TypedResults.NotFound();
        }

        Game? game = await database.Games
            .FirstOrDefaultAsync(row => row.Id == request.GameId, cancellationToken);
        if (game is null)
        {
            return TypedResults.Problem(
                title: "Unknown game",
                detail: $"Game {request.GameId} does not exist.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ProblemHttpResult? conflict =
            await AddAliasIfMissingAsync(database, unmatched.Source, unmatched.RawHomeName, game.HomeTeamId, cancellationToken)
            ?? await AddAliasIfMissingAsync(database, unmatched.Source, unmatched.RawAwayName, game.AwayTeamId, cancellationToken);

        if (conflict is not null)
        {
            return conflict;
        }

        if (unmatched.Source == ProviderSource.Espn && game.EspnEventId is null)
        {
            try
            {
                LiveScoreUpdate? payload =
                    JsonSerializer.Deserialize<LiveScoreUpdate>(unmatched.RawPayload, RawPayloadOptions);
                if (payload is not null
                    && long.TryParse(payload.SourceEventId, out long espnEventId))
                {
                    game.EspnEventId = espnEventId;
                }
            }
            catch (JsonException)
            {
                // The raw payload is kept verbatim for a human to read; a shape change there must
                // never block resolving the game, only skip learning the event id from it.
            }
        }

        unmatched.ResolvedUtc = timeProvider.GetUtcNow().UtcDateTime;

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Adds one <c>TeamAliases(Source, Alias)</c> row, or returns the 409 explaining why it
    /// cannot. <c>IX_TeamAliases_Source_Alias</c> is unique across teams, so an alias another
    /// team already owns is a conflict to report, not an exception to leak as a 500: the raw name
    /// and the chosen game disagree, and only a human can say which is wrong (D-089).
    /// </summary>
    private static async Task<ProblemHttpResult?> AddAliasIfMissingAsync(
        AppDbContext database,
        ProviderSource source,
        string alias,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        // Local first: the home and away calls share one change tracker, so a row added by the
        // earlier call is not in the database yet but would still violate the unique index.
        TeamAlias? existing = database.TeamAliases.Local
            .FirstOrDefault(row => row.Source == source && row.Alias == alias)
            ?? await database.TeamAliases
                .FirstOrDefaultAsync(row => row.Source == source && row.Alias == alias, cancellationToken);

        if (existing is not null)
        {
            return existing.TeamId == teamId
                ? null
                : TypedResults.Problem(
                    title: "Alias already assigned",
                    detail: $"'{alias}' is already a {source} alias of another team; "
                        + "remove that alias or pick the game whose team it belongs to.",
                    statusCode: StatusCodes.Status409Conflict);
        }

        database.TeamAliases.Add(new TeamAlias
        {
            Id = Guid.CreateVersion7(),
            TeamId = teamId,
            Source = source,
            Alias = alias,
        });

        return null;
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
