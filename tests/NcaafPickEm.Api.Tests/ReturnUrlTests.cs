using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The open-redirect guard on <c>/auth/login/google?returnUrl=</c>.
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
    public void GivenAnythingNotLocal_WhenSanitizing_ThenItFallsBackToTheRoot(string? candidate) =>
        ReturnUrl.Sanitize(candidate).Should().Be("/");
}
