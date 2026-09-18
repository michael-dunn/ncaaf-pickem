using System.Net;
using Microsoft.AspNetCore.Components;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Turns an API 401 into a trip to the login page. Unauthenticated <c>/api/*</c> calls return
/// 401 rather than a redirect (03-API-Contracts.md); the SPA is what decides where to send the
/// member, and it sends them back afterwards through <c>returnUrl</c>.
/// </summary>
/// <remarks>
/// The <c>/login</c> page itself is owned by P0-03. This handler only navigates to that route.
/// </remarks>
/// <param name="navigation">Navigation manager used to perform the redirect.</param>
public sealed class UnauthorizedRedirectHandler(NavigationManager navigation) : DelegatingHandler
{
    /// <summary>Route of the login page this handler redirects to.</summary>
    public const string LoginPath = "/login";

    private readonly NavigationManager _navigation = navigation;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        string current = _navigation.ToBaseRelativePath(_navigation.Uri);
        if (!current.StartsWith("login", StringComparison.OrdinalIgnoreCase))
        {
            string returnUrl = Uri.EscapeDataString("/" + current);
            _navigation.NavigateTo($"{LoginPath}?returnUrl={returnUrl}");
        }

        return response;
    }
}
