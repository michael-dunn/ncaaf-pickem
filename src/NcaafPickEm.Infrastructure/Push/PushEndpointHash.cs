using System.Security.Cryptography;
using System.Text;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// Computes the same value SQL Server stores in <c>PushSubscriptions.EndpointHash</c>, so a device
/// can be looked up by an index seek instead of a scan over <c>nvarchar(2048)</c>.
/// </summary>
/// <remarks>
/// The column is <c>CONVERT(binary(32), HASHBYTES('SHA2_256', [Endpoint]))</c> over an
/// <c>nvarchar</c>, which SQL Server hashes as UTF-16LE — exactly what
/// <see cref="Encoding.Unicode"/> produces (D-017 explains why the hash carries the unique index
/// in the first place). <c>PushSubscriptionTests</c> asserts the two agree against a real
/// database, so this cannot drift silently.
/// </remarks>
public static class PushEndpointHash
{
    /// <summary>The 32-byte SHA-256 of an endpoint URL, matching the computed column.</summary>
    /// <param name="endpoint">The push service URL, exactly as stored.</param>
    public static byte[] Compute(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return SHA256.HashData(Encoding.Unicode.GetBytes(endpoint));
    }
}
