using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Time;
using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Reads and moves the server's clock (P10-01, D-180): <c>GET</c>, <c>PUT</c> and <c>DELETE
/// /api/admin/fixture/clock</c>. Lets a browser session walk the app into the fixture week, past
/// the lock, and through Saturday without waiting for the calendar.
/// </summary>
/// <remarks>
/// Mapped only in Development and Testing (<see cref="EndpointMapping"/>), and only useful when
/// the registered <see cref="TimeProvider"/> is the <see cref="DevTimeProvider"/>
/// <c>AddInfrastructure</c> registers there; a host that swapped in another clock (the pinned
/// test factories) gets a 409 rather than a silent no-op.
/// </remarks>
public static class DevClockEndpoints
{
    /// <summary>Maps the three <c>/api/admin/fixture/clock</c> routes.</summary>
    public static RouteGroupBuilder MapDevClockEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder clock = api.MapGroup("/admin/fixture/clock")
            .WithTags("fixture-admin")
            .RequireAuthorization(PolicyNames.Authenticated);

        clock.MapGet("", GetAsync).WithName("DevClockGet");
        clock.MapPut("", SetAsync).WithName("DevClockSet");
        clock.MapDelete("", ResetAsync).WithName("DevClockReset");

        return api;
    }

    private static async Task<Results<Ok<DevClockResponse>, ProblemHttpResult>> GetAsync(
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        CancellationToken cancellationToken)
    {
        if (timeProvider is not DevTimeProvider clock)
        {
            return NotTheDevClock(timeProvider);
        }

        return TypedResults.Ok(await DescribeAsync(clock, weekSource, cancellationToken));
    }

    private static async Task<Results<Ok<DevClockResponse>, ProblemHttpResult>> SetAsync(
        DevClockRequest? request,
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        CancellationToken cancellationToken)
    {
        if (timeProvider is not DevTimeProvider clock)
        {
            return NotTheDevClock(timeProvider);
        }

        if (request is null || (request.NowUtc is null && request.Advance is null && request.Frozen is null))
        {
            return TypedResults.Problem(
                title: "Nothing to change",
                detail: "Set at least one of nowUtc, advance or frozen.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Freeze first, so "nowUtc + frozen" lands on exactly that instant rather than a few
        // microseconds past it; thaw last, so "nowUtc + frozen=false" runs on from that instant.
        if (request.Frozen is true)
        {
            clock.Freeze();
        }

        if (request.NowUtc is DateTimeOffset nowUtc)
        {
            clock.SetNow(nowUtc);
        }

        if (request.Advance is TimeSpan advance)
        {
            clock.Advance(advance);
        }

        if (request.Frozen is false)
        {
            clock.Thaw();
        }

        return TypedResults.Ok(await DescribeAsync(clock, weekSource, cancellationToken));
    }

    private static async Task<Results<Ok<DevClockResponse>, ProblemHttpResult>> ResetAsync(
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        CancellationToken cancellationToken)
    {
        if (timeProvider is not DevTimeProvider clock)
        {
            return NotTheDevClock(timeProvider);
        }

        clock.Reset();
        return TypedResults.Ok(await DescribeAsync(clock, weekSource, cancellationToken));
    }

    private static ProblemHttpResult NotTheDevClock(TimeProvider timeProvider) =>
        TypedResults.Problem(
            title: "The app clock is not the dev clock",
            detail: $"TimeProvider is {timeProvider.GetType().Name}; only DevTimeProvider can be moved.",
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// The clock plus where it puts the fixture season - the week and state a member would see -
    /// so the caller does not have to work that out from the instant.
    /// </summary>
    internal static async Task<DevClockResponse> DescribeAsync(
        DevTimeProvider clock,
        ISeasonWeekSource weekSource,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = clock.GetUtcNow();

        IReadOnlyList<SeasonWeek> weeks = await weekSource
            .GetWeeksAsync(FixtureReferenceDataProvider.FixtureSeason, cancellationToken);

        CurrentWeek? current = weeks.Count == 0 ? null : SeasonCalendar.CurrentWeekAt(nowUtc, weeks);

        return new DevClockResponse(
            nowUtc,
            clock.RealUtcNow,
            clock.Offset,
            clock.IsFrozen,
            clock.IsShifted,
            SeasonCalendar.EasternDisplay(nowUtc),
            current?.Week,
            current?.State.ToString());
    }
}
