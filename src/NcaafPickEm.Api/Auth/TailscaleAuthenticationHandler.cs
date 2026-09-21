using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NcaafPickEm.Domain.Users;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Authenticates a request from the identity headers <c>tailscale serve</c> injects: no cookie,
/// no session, one upsert per request (Phase 9).
/// </summary>
/// <remarks>
/// The tailnet is the security boundary, so <see cref="AuthDefaults.TailscaleLoginHeader"/> is
/// trusted exactly as it arrives (Phase 9, Q4). A request without it - a tagged device, a Funnel
/// caller, a health probe - is anonymous rather than refused, and the endpoint's own policy
/// decides what that means. The principal is the one
/// <see cref="ExternalSignInService.CreatePrincipal"/> builds, so nothing downstream can tell
/// this scheme apart from the cookie that <c>/auth/dev-login</c> writes.
/// </remarks>
public sealed class TailscaleAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Creates the handler.</summary>
    public TailscaleAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string login = Request.Headers[AuthDefaults.TailscaleLoginHeader].ToString().Trim();

        if (login.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        if (login.Length > User.ExternalSubjectMaxLength)
        {
            // Longer than the column can hold. Anonymous beats a 500 on every request.
            Logger.LogWarning(
                "Ignoring a {Header} of {Length} characters; the maximum is {Maximum}",
                AuthDefaults.TailscaleLoginHeader,
                login.Length,
                User.ExternalSubjectMaxLength);

            return AuthenticateResult.NoResult();
        }

        // Q2: the login is stored verbatim as both the match key and the email. Rfc2047.Decode
        // hands back whatever it cannot decode, so a malformed name header can never fail a
        // request; ToInitialDisplayName then falls back to the login's local part.
        string? name = Rfc2047.Decode(Request.Headers[AuthDefaults.TailscaleNameHeader].ToString());
        var externalLogin = new ExternalLogin(login, login, name);

        ExternalSignInService signIn = Context.RequestServices.GetRequiredService<ExternalSignInService>();
        User user = await signIn.UpsertAsync(externalLogin, Context.RequestAborted);

        ClaimsPrincipal principal = ExternalSignInService.CreatePrincipal(user, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
