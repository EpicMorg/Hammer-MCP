using System.Text.RegularExpressions;

namespace HammerMcp.Core.Maps;

/// <summary>
/// Hammer / Hammer++ autosaves: "&lt;map&gt;_NNN.vmf_autosave" in the autosave folder; versioninfo holds
/// autosave { "originalname" "&lt;path of the map&gt;" }, which is how an autosave is matched to its map.
/// </summary>
public static partial class Autosaves
{
    public sealed record Autosave(string Path, DateTime Written, int? MapVersion);

    /// <summary>Hammer++: [General] AutosaveDir / AutosaveEnabled in bin\hammerplusplus\hammerplusplus_settings.ini.</summary>
    public static (string? Dir, bool Enabled) HammerPlusPlusFolder(string hammerExe)
    {
        var ini = Path.Combine(Path.GetDirectoryName(hammerExe) ?? ".", "hammerplusplus", "hammerplusplus_settings.ini");
        if (!File.Exists(ini))
            return (null, false);
        string? dir = null;
        var enabled = true;
        var section = "";
        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
                section = line.Trim('[', ']');
            else if (section.Equals("General", StringComparison.OrdinalIgnoreCase) && line.Split('=', 2) is [var k, var v])
            {
                if (k.Trim().Equals("AutosaveDir", StringComparison.OrdinalIgnoreCase))
                    dir = v.Trim();
                else if (k.Trim().Equals("AutosaveEnabled", StringComparison.OrdinalIgnoreCase))
                    enabled = v.Trim() != "0";
            }
        }
        return (dir, enabled);
    }

    /// <summary>
    /// Strata Hammer: "Autosave Dir" / "Autosaves Enabled" in &lt;game&gt;\hammer\cfg\HammerConfig.vdf (KV3 text); a dir that
    /// starts with a backslash ("\\autosave\\") is relative to the hammer folder.
    /// </summary>
    public static (string? Dir, bool Enabled) StrataFolder(string hammerExe)
    {
        var hammerDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hammerExe) ?? ".", "..", "..", "hammer"));
        var vdf = Path.Combine(hammerDir, "cfg", "HammerConfig.vdf");
        if (!File.Exists(vdf))
            return (null, false);
        var text = File.ReadAllText(vdf);
        var dir = KvString().Match(text) is { Success: true } m ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
        var enabled = KvEnabled().Match(text) is not { Success: true } e || e.Groups[1].Value != "0";
        if (string.IsNullOrWhiteSpace(dir))
            return (null, enabled);
        if (dir.StartsWith('\\') || dir.StartsWith('/'))
            dir = Path.Combine(hammerDir, dir.Trim('\\', '/'));
        return (dir, enabled);
    }

    [GeneratedRegex(@"""Autosave Dir""\s*=\s*""([^""]*)""")]
    private static partial Regex KvString();

    [GeneratedRegex(@"""Autosaves Enabled""\s*=\s*(\d+)")]
    private static partial Regex KvEnabled();

    /// <summary>Autosaves of <paramref name="mapPath"/> in <paramref name="folder"/>, newest first.</summary>
    public static List<Autosave> ForMap(string folder, string mapPath)
    {
        if (!Directory.Exists(folder))
            return [];
        var name = Path.GetFileNameWithoutExtension(mapPath);
        var wanted = Normalize(mapPath);
        var result = new List<Autosave>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.vmf_autosave"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase))
                continue;
            var head = ReadHead(file);
            var original = OriginalNameRegex().Match(head);
            if (original.Success && Normalize(original.Groups[1].Value) != wanted)
                continue; // same map name, different folder
            int? version = MapVersionRegex().Match(head) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
            result.Add(new Autosave(file, File.GetLastWriteTime(file), version));
        }
        return [.. result.OrderByDescending(a => a.Written)];
    }

    /// <summary>The newest autosave if it is newer than the saved map, else null.</summary>
    public static Autosave? NewerThanMap(string folder, string mapPath)
    {
        var newest = ForMap(folder, mapPath).FirstOrDefault();
        return newest is not null && newest.Written > File.GetLastWriteTime(mapPath) ? newest : null;
    }

    private static string ReadHead(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[4096];
        return new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
    }

    private static string Normalize(string p) => p.Replace('\\', '/').Trim().ToLowerInvariant();

    [GeneratedRegex(@"""originalname""\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex OriginalNameRegex();

    [GeneratedRegex(@"""mapversion""\s+""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MapVersionRegex();
}
