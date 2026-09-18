using System.Globalization;
using System.Text;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Reduces a provider's spelling of a school to a comparable key
/// (04-Domain-Algorithms.md section 9): lower case, diacritics folded, punctuation dropped,
/// whitespace collapsed, and the noise word "university" removed.
/// </summary>
/// <remarks>
/// Diacritic folding is not optional: ESPN ships "San José State" with U+00E9, and lower-casing
/// alone leaves a key that never equals "san jose state" (D-012). Punctuation is dropped, not
/// turned into a separator, which is what makes "Hawai'i" and "Hawaii" the same key; whitespace
/// is the only separator, so "Miami (OH)" ("miami oh") still never collides with "Miami".
/// </remarks>
public static class TeamNameNormalizer
{
    private const string NoiseWord = "university";

    /// <summary>Normalizes a school name. Null, empty or all-noise input gives an empty key.</summary>
    /// <param name="value">The provider's spelling.</param>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append(' ');
            }
        }

        string[] words = builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(
            ' ',
            words.Where(word => !string.Equals(word, NoiseWord, StringComparison.Ordinal)));
    }
}
