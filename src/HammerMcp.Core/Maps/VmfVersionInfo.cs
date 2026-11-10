using System.Text.RegularExpressions;

namespace HammerMcp.Core.Maps;

/// <summary>The versioninfo block of a .vmf (only the fields the shell channel needs).</summary>
public sealed partial record VmfVersionInfo(int MapVersion, int? EditorVersion, int? EditorBuild)
{
    /// <summary>Reads versioninfo from the head of a .vmf without parsing the whole map.</summary>
    public static VmfVersionInfo Read(string vmfPath)
    {
        using var reader = new StreamReader(vmfPath);
        var head = new char[16 * 1024];
        var read = reader.ReadBlock(head, 0, head.Length);
        return Parse(new string(head, 0, read));
    }

    public static VmfVersionInfo Parse(string text)
    {
        var block = VersionInfoBlock().Match(text);
        if (!block.Success)
            throw new InvalidDataException("no versioninfo block");
        var body = block.Groups[1].Value;
        var mapVersion = Field(body, "mapversion")
                         ?? throw new InvalidDataException("versioninfo has no mapversion");
        return new VmfVersionInfo(mapVersion, Field(body, "editorversion"), Field(body, "editorbuild"));
    }

    private static int? Field(string body, string key)
    {
        var m = Regex.Match(body, $"\"{key}\"\\s+\"(-?\\d+)\"", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"versioninfo\s*\{([^}]*)\}", RegexOptions.IgnoreCase)]
    private static partial Regex VersionInfoBlock();
}
