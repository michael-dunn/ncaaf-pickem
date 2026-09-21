using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Drives the seeded demo league through the current week by hand (P10-01):
/// <c>/api/admin/fixture/demo</c> and its <c>generate</c>, <c>picks</c>, <c>lock</c>, <c>poll</c>
/// and <c>reset</c> controls. Every route answers with the same <see cref="DemoWeekResponse"/>
/// snapshot, with what it just did in <c>Notes</c>.
/// </summary>
/// <remarks>
/// Mapped only in Development and Testing (<see cref="EndpointMapping"/>). 404 when
/// <c>Seed:DemoLeague</c> has never been on, because there is nothing to drive.
/// </remarks>
public static class DemoWeekEndpoints
{
    /// <summary>Maps <c>/api/admin/fixture/demo</c> and its five controls.</summary>
    public static RouteGroupBuilder MapDemoWeekEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder demo = api.MapGroup("/admin/fixture/demo")
            .WithTags("fixture-admin")
            .RequireAuthorization(PolicyNames.Authenticated);

        demo.MapGet("", GetAsync).WithName("DemoWeekGet");
        demo.MapPost("/generate", GenerateAsync).WithName("DemoWeekGenerate");
        demo.MapPost("/picks", FillPicksAsync).WithName("DemoWeekFillPicks");
        demo.MapPost("/lock", LockAsync).WithName("DemoWeekLock");
        demo.MapPost("/poll", PollAsync).WithName("DemoWeekPoll");
        demo.MapPost("/reset", ResetAsync).WithName("DemoWeekReset");

        return api;
    }

    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> GetAsync(
        DemoWeekService demo,
        CancellationToken cancellationToken) =>
        Respond(await demo.GetAsync(cancellationToken));

    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> GenerateAsync(
        DemoWeekService demo,
        CancellationToken cancellationToken) =>
        Respond(await demo.GenerateAsync(cancellationToken));

    // includeMe=false (the default) leaves the caller's own picks alone so they can still be made by hand.
    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> FillPicksAsync(
        ICurrentUser currentUser,
        DemoWeekService demo,
        CancellationToken cancellationToken,
        bool includeMe = false) =>
        Respond(await demo.FillPicksAsync(includeMe ? null : currentUser.UserId, cancellationToken));

    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> LockAsync(
        DemoWeekService demo,
        CancellationToken cancellationToken) =>
        Respond(await demo.LockNowAsync(cancellationToken));

    // snapshot (1..6) moves the fixture score timeline first when given.
    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> PollAsync(
        int? snapshot,
        DemoWeekService demo,
        CancellationToken cancellationToken) =>
        Respond(await demo.PollAsync(snapshot, cancellationToken));

    private static async Task<Results<Ok<DemoWeekResponse>, NotFound<string>>> ResetAsync(
        DemoWeekService demo,
        CancellationToken cancellationToken) =>
        Respond(await demo.ResetAsync(cancellationToken));

    private static Results<Ok<DemoWeekResponse>, NotFound<string>> Respond(DemoWeekResponse? response) =>
        response is null
            ? TypedResults.NotFound(
                "No demo league. Set Seed__DemoLeague=true (with Providers__ReferenceData=Fixture) and restart " +
                "so FixtureSeeder creates the Family League.")
            : TypedResults.Ok(response);
}
