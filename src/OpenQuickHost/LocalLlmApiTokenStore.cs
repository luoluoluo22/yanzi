using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost;

/// <summary>
/// Per-Windows-user LLM-only credential. It deliberately does not rotate the
/// existing Agent API token or sync between devices. Provider API keys stay in
/// the independent AI credential vault.
/// </summary>
internal static class LocalLlmApiTokenStore
{
    private static readonly object Gate = new();
    private static string SecretPath =>
        HostAssets.ResolveDataFilePath("llm-api-token.dat");

    internal static string LoadOrCreate()
    {
        lock (Gate)
        {
            if (File.Exists(SecretPath))
            {
                // Fail closed for corrupt/unreadable vaults; never silently overwrite
                // a credential that other locally installed clients might be using.
                var protectedBytes = File.ReadAllBytes(SecretPath);
                var bytes = ProtectedData.Unprotect(protectedBytes, null,
                    DataProtectionScope.CurrentUser);
                var saved = Encoding.UTF8.GetString(bytes);
                if (saved.Length != 64 || !saved.All(Uri.IsHexDigit))
                    throw new CryptographicException("LLM API token vault is invalid.");
                return saved;
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null,
                DataProtectionScope.CurrentUser);
            var dir = Path.GetDirectoryName(SecretPath)!;
            Directory.CreateDirectory(dir);
            var temp = SecretPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, cipher);
                File.Move(temp, SecretPath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return token;
        }
    }

    internal static bool IsAuthorized(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        if (authorization == null ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var supplied = authorization["Bearer ".Length..].Trim();
        if (supplied.Length != 64 || !supplied.All(Uri.IsHexDigit)) return false;
        // Constant-time comparison keeps the secret independent of the Agent token.
        var expected = Encoding.ASCII.GetBytes(LoadOrCreate());
        var provided = Encoding.ASCII.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
