using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Signs a request in as whatever user id the <c>X-Test-User</c> header names, so no test ever
/// has to produce a real identity.
/// </summary>
/// <remarks>
/// The handler does not create users: tests seed rows through <see cref="TestUsers"/> and pass the
/// resulting id, which keeps the database the single source of truth about who exists. No header,
/// no result — the request stays anonymous and the endpoint answers 401 exactly as in production.
/// Claim shapes match <c>ExternalSignInService.CreatePrincipal</c>, so <c>ICurrentUser</c> cannot
/// tell the two schemes apart.
/// </remarks>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Scheme name. Registered as the default scheme by <see cref="ApiFactory"/>.</summary>
    public const string SchemeName = "TestAuth";

    /// <summary>Header naming the signed-in user's <c>Users.Id</c>.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>Optional header overriding the display-name claim.</summary>
    public const string NameHeader = "X-Test-Name";

    /// <summary>Creates the handler.</summary>
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var raw)
            || !Guid.TryParse(raw.ToString(), out Guid userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string displayName = Request.Headers.TryGetValue(NameHeader, out var name) && name.Count > 0
            ? name.ToString()
            : "Test User";

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.Email, $"{userId:N}@test.local"),
            ],
            SchemeName,
            ClaimTypes.Name,
            ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
