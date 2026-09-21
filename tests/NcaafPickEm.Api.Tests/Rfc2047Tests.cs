using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The RFC 2047 decoder behind <c>Tailscale-User-Name</c> (P9-02).
/// </summary>
public sealed class Rfc2047Tests
{
    [Fact]
    public void GivenNull_WhenDecoding_ThenItIsNull() =>
        Rfc2047.Decode(null).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("Alice Example")]
    [InlineData("Ferris Buller")]
    [InlineData("  spaced  out  ")]
    public void GivenPlainAscii_WhenDecoding_ThenItPassesThrough(string value) =>
        Rfc2047.Decode(value).Should().Be(value);

    [Fact]
    public void GivenAQEncodedWordWithUnderscores_WhenDecoding_ThenUnderscoresBecomeSpaces() =>
        Rfc2047.Decode("=?utf-8?q?Alice_Example?=").Should().Be("Alice Example");

    [Fact]
    public void GivenAQEncodedWordWithHexEscapes_WhenDecoding_ThenTheBytesAreUtf8() =>
        Rfc2047.Decode("=?utf-8?q?Ferris_B=C3=BCller?=").Should().Be("Ferris Büller");

    [Fact]
    public void GivenAnUppercaseQAndCharset_WhenDecoding_ThenItIsStillDecoded() =>
        Rfc2047.Decode("=?UTF-8?Q?Zo=C3=AB?=").Should().Be("Zoë");

    [Fact]
    public void GivenABEncodedWord_WhenDecoding_ThenTheBase64IsUtf8() =>
        Rfc2047.Decode("=?utf-8?b?RmVycmlzIELDvGxsZXI=?=").Should().Be("Ferris Büller");

    [Fact]
    public void GivenTwoEncodedWords_WhenDecoding_ThenTheFoldingSpaceBetweenThemIsDropped() =>
        Rfc2047.Decode("=?utf-8?q?Ferris_?= =?utf-8?b?QsO8bGxlcg==?=").Should().Be("Ferris Büller");

    [Fact]
    public void GivenAnEncodedWordBesidePlainText_WhenDecoding_ThenThePlainTextAndItsSpacingSurvive() =>
        Rfc2047.Decode("Dr. =?utf-8?q?Zo=C3=AB?= (owner)").Should().Be("Dr. Zoë (owner)");

    [Theory]

    // A truncated =XX escape, invalid base64, an unsupported charset, an unsupported encoding,
    // and a bare "=?" that never becomes an encoded word at all.
    [InlineData("=?utf-8?q?bad=Z?=")]
    [InlineData("=?utf-8?q?trailing=?=")]
    [InlineData("=?utf-8?b?not base64!?=")]
    [InlineData("=?iso-8859-1?q?caf=E9?=")]
    [InlineData("=?utf-8?x?whatever?=")]
    [InlineData("=?utf-8?q?unterminated")]
    [InlineData("=?")]
    public void GivenSomethingMalformed_WhenDecoding_ThenTheRawInputComesBack(string value) =>
        Rfc2047.Decode(value).Should().Be(value);

    [Fact]
    public void GivenOneGoodAndOneBadWord_WhenDecoding_ThenTheWholeRawInputComesBack()
    {
        const string value = "=?utf-8?q?Zo=C3=AB?= =?iso-8859-1?q?caf=E9?=";

        Rfc2047.Decode(value).Should().Be(value);
    }
}
