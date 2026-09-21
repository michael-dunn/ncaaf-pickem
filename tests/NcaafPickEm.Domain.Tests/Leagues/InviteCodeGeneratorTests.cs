using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Domain.Tests.Leagues;

/// <summary>
/// <see cref="InviteCodeGenerator"/> (Feature 01, P9-05): six digits, leading zeros allowed.
/// </summary>
public sealed class InviteCodeGeneratorTests
{
    [Fact]
    public void GivenGenerate_WhenCalled_ThenTheCodeIsSixCharactersLong()
    {
        string code = InviteCodeGenerator.Generate();

        code.Length.Should().Be(InviteCodeGenerator.Length);
        code.Length.Should().Be(6);
    }

    [Fact]
    public void GivenGenerate_WhenCalledManyTimes_ThenEveryCodeIsAllDigits()
    {
        for (int i = 0; i < 2_000; i++)
        {
            string code = InviteCodeGenerator.Generate();
            code.Should().HaveLength(6);
            code.Should().MatchRegex("^[0-9]{6}$");
        }
    }

    [Fact]
    public void GivenGenerate_WhenCalledManyTimes_ThenEveryCodeParsesIntoTheFullRange()
    {
        // Over a large sample, at least one code starting with '0' is not guaranteed, so this
        // asserts the invariant that IS guaranteed: every code is a six-character digit string
        // whose integer value sits in [0, 999999], and D6 formatting preserves any leading zeros
        // (i.e. the string is always 6 characters, never a shorter numeral).
        HashSet<string> lengths = [];
        for (int i = 0; i < 2_000; i++)
        {
            string code = InviteCodeGenerator.Generate();
            lengths.Add(code.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

            int value = int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
            value.Should().BeInRange(0, 999_999);
        }

        lengths.Should().BeEquivalentTo(["6"], "every code must be exactly six characters regardless of its numeric value");
    }

    [Fact]
    public void GivenGenerate_WhenCalledTwice_ThenConsecutiveCodesDifferOverManySamples()
    {
        bool sawADifference = false;
        string previous = InviteCodeGenerator.Generate();
        for (int i = 0; i < 100; i++)
        {
            string next = InviteCodeGenerator.Generate();
            if (next != previous)
            {
                sawADifference = true;
            }

            previous = next;
        }

        sawADifference.Should().BeTrue("a cryptographically random generator should not produce the same code every time over 100 samples");
    }
}
