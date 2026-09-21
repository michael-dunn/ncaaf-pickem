using System.Globalization;
using System.Security.Cryptography;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Generates six-digit invite codes that are easy to read aloud, retype from a text message, or
/// type on a phone keypad (Feature 01, P9-05).
/// </summary>
public static class InviteCodeGenerator
{
    /// <summary>Length of a generated code.</summary>
    public const int Length = 6;

    /// <summary>Creates one cryptographically random <see cref="Length"/>-digit code, leading zeros allowed.</summary>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
