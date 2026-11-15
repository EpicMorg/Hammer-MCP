using System.Security.Cryptography;
using System.Text;

namespace HammerMcp.App;

/// <summary>
/// Secrets in settings.json (the server token, the OpenCode password) are kept encrypted with Windows DPAPI for the
/// current user: "dpapi:" + base64. Another Windows user (or a copy of the file on another machine) cannot read them.
/// </summary>
internal static class SettingsSecret
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = "EpicMorg Hammer-MCP settings"u8.ToArray();

    public static string Protect(string plain) => plain.Length == 0
        ? ""
        : Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The plain text; "" when it cannot be decrypted (another user / machine) - the user sets it again.</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored ?? ""; // not encrypted (should not happen: plain values live in the old keys)
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return "";
        }
    }
}
