using WebPush;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// Generates a VAPID key pair and prints it in the shape the configuration expects.
/// </summary>
/// <remarks>
/// Reached from the Api's hidden <c>generate-vapid</c> argument (<c>dotnet run --project
/// src/NcaafPickEm.Api -- generate-vapid</c>), which <c>deploy/generate-vapid.ps1</c> wraps. The
/// keys are printed and never written to disk: whoever runs it decides where the private key
/// lives, and nothing in the repo should ever hold one.
/// </remarks>
public static class VapidKeyGenerator
{
    /// <summary>The command-line argument that triggers generation instead of starting the app.</summary>
    public const string CommandName = "generate-vapid";

    /// <summary>A fresh base64url-encoded P-256 key pair.</summary>
    public static (string PublicKey, string PrivateKey) Generate()
    {
        VapidDetails details = VapidHelper.GenerateVapidKeys();
        return (details.PublicKey, details.PrivateKey);
    }

    /// <summary>Writes a fresh key pair as <c>Push__*=value</c> lines.</summary>
    /// <param name="output">Where to write, normally <see cref="Console.Out"/>.</param>
    public static void WriteNewKeyPair(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        (string publicKey, string privateKey) = Generate();

        output.WriteLine($"Push__VapidPublicKey={publicKey}");
        output.WriteLine($"Push__VapidPrivateKey={privateKey}");
    }
}
