using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NcaafPickEm.Api.Auth;

/// <summary>
/// Decodes the RFC 2047 "encoded word" form Tailscale uses for a non-ASCII
/// <c>Tailscale-User-Name</c> header, e.g. <c>=?utf-8?q?Ferris_B=C3=BCller?=</c>.
/// </summary>
/// <remarks>
/// Deliberately narrow and deliberately forgiving. Only the <c>utf-8</c> charset with the Q and B
/// encodings is understood, because that is the only thing Tailscale emits; anything else - an
/// unknown charset, a truncated escape, invalid base64 - hands the raw header value straight back
/// rather than throwing, because a display name is never worth failing a request over. Plain
/// text with no encoded word in it passes through untouched.
/// </remarks>
public static partial class Rfc2047
{
    private const string Utf8Charset = "utf-8";

    /// <summary>
    /// Decodes every <c>=?utf-8?q?..?=</c> / <c>=?utf-8?b?..?=</c> word in <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The raw header value, or null.</param>
    /// <returns>
    /// Null when <paramref name="value"/> is null; the decoded text when every encoded word in it
    /// could be decoded; otherwise <paramref name="value"/> unchanged.
    /// </returns>
    public static string? Decode(string? value)
    {
        if (value is null || !value.Contains("=?", StringComparison.Ordinal))
        {
            return value;
        }

        MatchCollection matches = EncodedWord().Matches(value);
        if (matches.Count == 0)
        {
            return value;
        }

        var decoded = new StringBuilder(value.Length);
        int position = 0;
        bool previousWasEncodedWord = false;

        foreach (Match match in matches)
        {
            string between = value[position..match.Index];

            // RFC 2047 section 6.2: whitespace separating two adjacent encoded words is a
            // folding artifact and is dropped. Whitespace next to plain text is real.
            bool isFoldingWhitespace = previousWasEncodedWord
                && between.Length > 0
                && string.IsNullOrWhiteSpace(between);

            if (!isFoldingWhitespace)
            {
                decoded.Append(between);
            }

            string? word = DecodeWord(match);
            if (word is null)
            {
                return value;
            }

            decoded.Append(word);
            position = match.Index + match.Length;
            previousWasEncodedWord = true;
        }

        decoded.Append(value[position..]);
        return decoded.ToString();
    }

    private static string? DecodeWord(Match match)
    {
        if (!string.Equals(match.Groups["charset"].Value, Utf8Charset, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string text = match.Groups["text"].Value;

        try
        {
            byte[] bytes = match.Groups["encoding"].Value is "b" or "B"
                ? Convert.FromBase64String(text)
                : DecodeQuotedPrintable(text);

            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] DecodeQuotedPrintable(string text)
    {
        var bytes = new List<byte>(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];

            switch (current)
            {
                case '_':
                    bytes.Add((byte)' ');
                    break;

                case '=':
                    if (index + 2 >= text.Length)
                    {
                        throw new FormatException("Truncated =XX escape in a Q-encoded word.");
                    }

                    bytes.Add(byte.Parse(
                        text.AsSpan(index + 1, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                    index += 2;
                    break;

                default:
                    if (current > 0x7F)
                    {
                        throw new FormatException("A Q-encoded word may only hold ASCII.");
                    }

                    bytes.Add((byte)current);
                    break;
            }
        }

        return [.. bytes];
    }

    [GeneratedRegex(@"=\?(?<charset>[^?\s]+)\?(?<encoding>[QqBb])\?(?<text>[^?\s]*)\?=", RegexOptions.CultureInvariant)]
    private static partial Regex EncodedWord();
}
