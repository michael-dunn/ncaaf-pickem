using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The open-redirect guard on <c>/auth/dev-login?returnUrl=</c>.
/// </summary>
public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/leagues/mine")]
    [InlineData("/leagues/mine?week=7")]
    public void GivenALocalPath_WhenSanitizing_ThenItIsKept(string candidate) =>
        ReturnUrl.Sanitize(candidate).Should().Be(candidate);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example.com/steal")]
    [InlineData("http://evil.example.com")]
    [InlineData("//evil.example.com/steal")]
    [InlineData("/\\evil.example.com")]
    [InlineData("leagues/mine")]
    [InlineData("javascript:alert(1)")]

    // P8-01 additions.
    [InlineData("//evil.example.com")]
    [InlineData("/\\\\evil.example.com")]
    [InlineData("\\\\evil.example.com")]
    [InlineData("\\/evil.example.com")]
    [InlineData("HTTPS://evil.example.com/steal")]
    [InlineData("Https://evil.example.com/steal")]
    [InlineData("  //evil.example.com")]
    [InlineData("/\t/evil.example.com")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("JavaScript:alert(1)")]

    // Response splitting: a CR or LF would otherwise land in the Location header.
    [InlineData("/leagues\r\nSet-Cookie: stolen=1")]
    [InlineData("/leagues\nX-Injected: 1")]
    public void GivenAnythingNotLocal_WhenSanitizing_ThenItFallsBackToTheRoot(string? candidate) =>
        ReturnUrl.Sanitize(candidate).Should().Be("/");

    /// <summary>
    /// A percent-encoded protocol-relative URL is decoded by query binding before it ever reaches
    /// <see cref="ReturnUrl.Sanitize"/>, so it must be refused in its decoded form.
    /// </summary>
    [Theory]
    [InlineData("%2F%2Fevil.example.com")]
    [InlineData("%2f%2fevil.example.com")]
    [InlineData("%2F%5Cevil.example.com")]
    public void GivenAPercentEncodedProtocolRelativeUrl_WhenDecodedAndSanitized_ThenItFallsBackToTheRoot(
        string encoded) =>
        ReturnUrl.Sanitize(Uri.UnescapeDataString(encoded)).Should().Be("/");
}
