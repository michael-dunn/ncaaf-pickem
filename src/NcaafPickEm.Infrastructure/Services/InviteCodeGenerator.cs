using System.Security.Cryptography;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Generates short, URL-safe invite codes that are easy to read aloud or retype from a text
/// message (Feature 01).
/// </summary>
public static class InviteCodeGenerator
{
    /// <summary>Length of a generated code.</summary>
    public const int Length = 8;

    // No 0/O or 1/l/I: characters that are easy to confuse when hand-typing a code from a text
    // message. Upper-case only, so a code is never mistyped over case.
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Creates one cryptographically random <see cref="Length"/>-character code.</summary>
    public static string Generate()
    {
        Span<char> buffer = stackalloc char[Length];
        for (int i = 0; i < Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }
}
