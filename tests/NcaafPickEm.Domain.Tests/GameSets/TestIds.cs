using System.Security.Cryptography;
using System.Text;

namespace NcaafPickEm.Domain.Tests.GameSets;

/// <summary>
/// Turns a name into a stable Guid so tests can say "Alabama" instead of carrying id variables
/// around, and so a failing assertion is reproducible between runs.
/// </summary>
internal static class TestIds
{
    public static Guid Of(string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));

        return new Guid(hash.AsSpan(0, 16));
    }
}
