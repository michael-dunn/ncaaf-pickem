using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Sign-in and sign-out, at the root rather than under <c>/api</c> because they are browser
/// navigations, not SPA fetches (03-API-Contracts.md, Auth).
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Maps <c>/auth/login/google</c> and <c>/auth/logout</c>.</summary>
    /// <remarks>
    /// <c>/auth/callback/google</c> has no endpoint on purpose: it is the Google handler's
    /// <c>CallbackPath</c>, so the authentication middleware answers it before routing ever runs.
    /// </remarks>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder auth = builder.MapGroup("/auth")
            .WithTags("auth")

            // P8-01: a generous fixed window per client IP. /auth is the one route family a
            // caller can reach with no cookie at all.
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy);

        auth.MapGet("/login/google", LoginWithGoogle)
            .WithName("AuthLoginGoogle")
            .AllowAnonymous();

        // Cast to Delegate: a handler whose only parameter is HttpContext and that returns a Task
        // would otherwise bind as a RequestDelegate, and the result would be discarded (ASP0016).
        auth.MapPost("/logout", (Delegate)LogoutAsync)
            .WithName("AuthLogout")
            .RequireAuthorization(PolicyNames.Authenticated);

        return builder;
    }

    /// <summary>
    /// Starts the Google redirect. <c>returnUrl</c> must be a relative path; anything else is
    /// replaced with the site root rather than refused, so a stale bookmark still signs in.
    /// </summary>
    private static ChallengeHttpResult LoginWithGoogle(string? returnUrl) =>
        TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) },
            [GoogleDefaults.AuthenticationScheme]);

    /// <summary>
    /// Clears the session cookie. The server holds no session state, so deleting the cookie is
    /// the whole of it.
    /// </summary>
    private static async Task<NoContent> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}
