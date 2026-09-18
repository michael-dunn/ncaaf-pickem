using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for Google's token and userinfo endpoints so the real OAuth handler can run offline.
/// </summary>
/// <remarks>
/// This is deliberately not a stub of our own callback: the point is to exercise the shipped
/// pipeline — challenge, state and correlation cookie, code exchange, claim mapping,
/// <c>OnTicketReceived</c> — with only the network replaced.
/// </remarks>
public sealed class FakeGoogleBackchannel : HttpMessageHandler
{
    private readonly string _subject;
    private readonly string _email;
    private readonly string? _name;

    /// <summary>Creates a handler that returns one fixed Google profile.</summary>
    public FakeGoogleBackchannel(string subject, string email, string? name)
    {
        _subject = subject;
        _email = email;
        _name = name;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string path = request.RequestUri?.AbsoluteUri ?? string.Empty;

        object payload = path.Contains("userinfo", StringComparison.OrdinalIgnoreCase)
            ? new { sub = _subject, email = _email, email_verified = true, name = _name }
            : new { access_token = "fake-access-token", token_type = "Bearer", expires_in = 3600 };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload)),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return Task.FromResult(response);
    }
}
