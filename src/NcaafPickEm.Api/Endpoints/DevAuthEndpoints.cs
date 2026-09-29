using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Signs in as a fixture demo user or a simulator player without a tailnet, so a UI or manual
/// check can run from a plain browser (P2-05, P11-01).
/// </summary>
/// <remarks>
/// Mapped only in Development and Testing (<see cref="EndpointMapping"/>). Looks up
/// <c>Users.ExternalSubject = "fixture:{user}"</c> — the rows <c>FixtureSeeder</c> creates when
/// <c>Seed:DemoLeague</c> is true — and then <c>"sim:{user}"</c>, the players
/// <c>tools/NcaafPickEm.Simulator</c> creates. Either way it issues the cookie principal
/// <see cref="ExternalSignInService.CreatePrincipal"/> builds, so every downstream authorization
/// check behaves exactly as it does for a request carrying a Tailscale identity header. Only
/// those two prefixes are ever looked up, so no real Tailscale login can be signed in as.
/// </remarks>
public static class DevAuthEndpoints
{
    /// <summary>Subject prefix of the users <c>FixtureSeeder</c> creates.</summary>
    public const string FixtureSubjectPrefix = "fixture:";

    /// <summary>Subject prefix of the players the simulator creates (D-184).</summary>
    public const string SimulatorSubjectPrefix = "sim:";

    private static readonly string[] FixtureUserNames = ["michael", "alyson", "dance", "alex", "daniel"];

    /// <summary>Maps <c>GET /auth/dev-login</c>.</summary>
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/auth/dev-login", DevLoginAsync)
            .WithName("AuthDevLogin")

            // P8-01: a generous fixed window per client IP. This is the only route under /auth,
            // and the only one a caller can reach with no identity at all.
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy)
            .AllowAnonymous();

        return builder;
    }

    private static async Task<Results<RedirectHttpResult, NotFound<string>, BadRequest<string>>> DevLoginAsync(
        string? user,
        string? returnUrl,
        HttpContext httpContext,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return TypedResults.BadRequest(
                "Pass ?user=<name>, e.g. /auth/dev-login?user=michael. " +
                $"Fixture demo users: {string.Join(", ", FixtureUserNames)}; simulator players by their first name.");
        }

        string name = user.Trim().ToLowerInvariant();
        string fixtureSubject = FixtureSubjectPrefix + name;
        string simulatorSubject = SimulatorSubjectPrefix + name;

        // Fixture first, so a database holding both kinds signs in the way it always has.
        User? account = await database.Users
            .Where(candidate => candidate.ExternalSubject == fixtureSubject || candidate.ExternalSubject == simulatorSubject)
            .OrderBy(candidate => candidate.ExternalSubject == fixtureSubject ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound(
                $"No fixture or simulator user '{user}'. For the fixture demo league set " +
                "Providers__ReferenceData=Fixture and Seed__DemoLeague=true and restart (users: " +
                $"{string.Join(", ", FixtureUserNames)}); for a simulated league create the player in " +
                "tools/NcaafPickEm.Simulator first.");
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ExternalSignInService.CreatePrincipal(account, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });

        return TypedResults.Redirect(ReturnUrl.Sanitize(returnUrl));
    }
}
