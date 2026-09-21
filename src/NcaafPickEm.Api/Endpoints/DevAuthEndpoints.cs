using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Signs in as one of the fixture demo users without Google, so a UI or manual check can run
/// against a full week of fixture data with no OAuth client at all (P2-05).
/// </summary>
/// <remarks>
/// Mapped only in Development (<see cref="EndpointMapping"/>). Looks up
/// <c>Users.ExternalSubject = "fixture:{user}"</c> — the same rows <c>FixtureSeeder</c> creates when
/// <c>Seed:DemoLeague</c> is true — and issues the identical cookie principal
/// <see cref="ExternalSignInService.CreatePrincipal"/> builds for a real Google sign-in, so every
/// downstream authorization check behaves the same either way.
/// </remarks>
public static class DevAuthEndpoints
{
    /// <summary>Maps <c>GET /auth/dev-login</c>.</summary>
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/auth/dev-login", DevLoginAsync)
            .WithName("AuthDevLogin")
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
                $"Fixture demo users: {string.Join(", ", FixtureUserNames)}.");
        }

        string subject = $"fixture:{user.Trim().ToLowerInvariant()}";

        User? account = await database.Users
            .FirstOrDefaultAsync(candidate => candidate.ExternalSubject == subject, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound(
                $"No fixture user '{user}'. Set Providers__ReferenceData=Fixture and Seed__DemoLeague=true " +
                "and restart so FixtureSeeder creates the demo league members, or pass one of: " +
                $"{string.Join(", ", FixtureUserNames)}.");
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ExternalSignInService.CreatePrincipal(account, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });

        return TypedResults.Redirect(ReturnUrl.Sanitize(returnUrl));
    }

    private static readonly string[] FixtureUserNames = ["michael", "alyson", "dance", "alex", "daniel"];
}
