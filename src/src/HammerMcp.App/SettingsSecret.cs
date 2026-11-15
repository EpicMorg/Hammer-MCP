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

    /// <summary>The plain text; false (and "") when it cannot be decrypted - another Windows user or machine.</summary>
    public static bool TryUnprotect(string? stored, out string plain)
    {
        plain = "";
        if (string.IsNullOrEmpty(stored))
            return true;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            plain = stored; // not encrypted (hand-edited file)
            return true;
        }
        try
        {
            plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
