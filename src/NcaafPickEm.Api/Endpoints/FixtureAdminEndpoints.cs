using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Infrastructure.Providers.Fixture;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Reads and advances the fixture live-score timeline (P2-05). Lets a UI or manual check step
/// through kickoff-to-final without waiting for a real Saturday, and is what P2-04's poller test
/// hook drives in integration tests.
/// </summary>
/// <remarks>Mapped only in Development (<see cref="EndpointMapping"/>).</remarks>
public static class FixtureAdminEndpoints
{
    /// <summary>Maps <c>/api/admin/fixture/snapshot</c>.</summary>
    public static RouteGroupBuilder MapFixtureAdminEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder fixtureAdmin = api.MapGroup("/admin/fixture")
            .WithTags("fixture-admin")
            .RequireAuthorization(PolicyNames.Authenticated);

        fixtureAdmin.MapGet("/snapshot", GetSnapshot).WithName("FixtureSnapshotGet");
        fixtureAdmin.MapPost("/snapshot/{n:int}", SetSnapshot).WithName("FixtureSnapshotSet");

        return api;
    }

    private static Ok<int> GetSnapshot(FixtureSnapshotState snapshotState) =>
        TypedResults.Ok(snapshotState.Current);

    private static Results<Ok<int>, BadRequest<string>> SetSnapshot(int n, FixtureSnapshotState snapshotState)
    {
        if (n < FixtureSnapshotState.MinSnapshot || n > FixtureSnapshotState.MaxSnapshot)
        {
            return TypedResults.BadRequest(
                $"Snapshot must be between {FixtureSnapshotState.MinSnapshot} and {FixtureSnapshotState.MaxSnapshot}.");
        }

        snapshotState.Set(n);
        return TypedResults.Ok(snapshotState.Current);
    }
}
