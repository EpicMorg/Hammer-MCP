namespace HammerMcp.Core.Maps;

/// <summary>Parses the open map path out of a Hammer main window title.</summary>
public static class HammerWindowTitle
{
    private static readonly string[] MapExtensions = [".vmap", ".vmf", ".vmm", ".vmx", ".rmf", ".map", ".jmf"];

    // Source 2 Hammer (Qt) does not use the "[path - view]" form; fall back to any absolute path with a map extension
    private static readonly System.Text.RegularExpressions.Regex AnyMapPath =
        new(@"[A-Za-z]:[\\/][^\[\]*?""<>|]*?\.(vmap|vmf|vmm|vmx|rmf|map|jmf)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"Hammer++ - 64 Bit - [B:\...\map.vmf - Textured Shaded]" → "B:\...\map.vmf"; null if no map in the title.</summary>
    public static string? ParseMapPath(string title)
    {
        var open = title.IndexOf('[');
        if (open < 0)
            return AnyMapPath.Match(title) is { Success: true } m ? m.Value : null;
        var inner = title[(open + 1)..].TrimEnd(']');
        foreach (var ext in MapExtensions)
        {
            var at = inner.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (at > 0)
                return inner[..(at + ext.Length)];
        }
        return null;
    }
}
