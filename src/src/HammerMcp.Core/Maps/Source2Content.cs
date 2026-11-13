using System.Collections.Concurrent;

namespace HammerMcp.Core.Maps;

/// <summary>
/// Source 2 Hammer shows only the map file name in its title ("cs_script_demo.vmap"). Maps live in the content tree
/// next to game/: content/&lt;mod&gt;/maps/... or content/&lt;mod&gt;_addons/&lt;addon&gt;/maps/...; this finds the file there.
/// </summary>
public static class Source2Content
{
    private static readonly ConcurrentDictionary<string, (string? Path, DateTime At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);

    /// <summary>content/ of the Source 2 install whose game exe is <paramref name="exePath"/> (game/bin/win64/x.exe), if it exists.</summary>
    public static string? ContentRoot(string exePath)
    {
        var bin = Path.GetDirectoryName(exePath);
        if (bin is null || !File.Exists(Path.Combine(bin, "tools", "hammer.dll")))
            return null;
        var content = Path.GetFullPath(Path.Combine(bin, "..", "..", "..", "content"));
        return Directory.Exists(content) ? content : null;
    }

    /// <summary>
    /// Full path of <paramref name="mapName"/> (file name or path relative to a mod / addon folder) under the content tree;
    /// the most recently written match wins. Null if not found or not a Source 2 install.
    /// </summary>
    public static string? FindMap(string exePath, string mapName)
    {
        if (Path.IsPathRooted(mapName))
            return File.Exists(mapName) ? mapName : null;
        var key = $"{exePath}|{mapName}";
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheTime && (hit.Path is null || File.Exists(hit.Path)))
            return hit.Path;
        var found = Search(exePath, mapName);
        Cache[key] = (found, DateTime.UtcNow);
        return found;
    }

    private static string? Search(string exePath, string mapName)
    {
        if (ContentRoot(exePath) is not { } content)
            return null;
        var relative = mapName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(relative);
        var roots = new List<string>();
        foreach (var mod in SafeDirs(content))
        {
            if (Path.GetFileName(mod).EndsWith("_addons", StringComparison.OrdinalIgnoreCase))
                roots.AddRange(SafeDirs(mod));
            else
                roots.Add(mod);
        }
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            if (relative.Contains(Path.DirectorySeparatorChar))
            {
                var direct = Path.Combine(root, relative);
                if (File.Exists(direct))
                    candidates.Add(direct);
                continue;
            }
            var maps = Path.Combine(root, "maps");
            if (!Directory.Exists(maps))
                continue;
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(maps, fileName, new EnumerationOptions
                {
                    RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true,
                }));
            }
            catch (IOException)
            {
            }
        }
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Folder of Source 2 Hammer autosaves for a map: content\_toolsautosave\hammer\&lt;map folder relative to content&gt;
    /// (e.g. content\csgo_addons\x\maps → content\_toolsautosave\hammer\csgo_addons\x\maps). Null if the map is not under content\.
    /// </summary>
    public static string? AutosaveFolder(string mapPath)
    {
        var full = Path.GetFullPath(mapPath);
        var dir = Path.GetDirectoryName(full);
        var parts = (dir ?? "").Split(Path.DirectorySeparatorChar);
        var at = Array.FindLastIndex(parts, p => p.Equals("content", StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return null;
        var content = string.Join(Path.DirectorySeparatorChar, parts[..(at + 1)]);
        var rest = parts[(at + 1)..];
        return Path.Combine([content, "_toolsautosave", "hammer", .. rest]);
    }

    /// <summary>Newest autosave of <paramref name="mapPath"/> ("&lt;map&gt;NNN.vmap_autosave" in <see cref="AutosaveFolder"/>), if newer than the map.</summary>
    public static string? NewestAutosave(string mapPath)
    {
        if (AutosaveFolder(mapPath) is not { } folder || !Directory.Exists(folder))
            return null;
        var stem = Path.GetFileNameWithoutExtension(mapPath);
        var saved = File.Exists(mapPath) ? File.GetLastWriteTimeUtc(mapPath) : DateTime.MinValue;
        return Directory.EnumerateFiles(folder)
            // "<map>NNN.vmap_autosave", e.g. cs_script_demo002.vmap_autosave (seen on CS2)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f),
                $@"^{System.Text.RegularExpressions.Regex.Escape(stem)}\d*\.vmap(_autosave)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Where(f => File.GetLastWriteTimeUtc(f) > saved)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>An autosave path mapped back to where the map lives (content\_toolsautosave\hammer\x → content\x), else unchanged.</summary>
    public static string FromAutosaveTree(string path)
    {
        var marker = $"{Path.DirectorySeparatorChar}_toolsautosave{Path.DirectorySeparatorChar}hammer{Path.DirectorySeparatorChar}";
        var at = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? path : path[..at] + Path.DirectorySeparatorChar + path[(at + marker.Length)..];
    }

    private static readonly ConcurrentDictionary<string, (string? Path, DateTime At)> MapDirCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Source 1 / Strata Hammer showing only a file name in its title: the file in the MapDir of one of the Hammer's game
    /// configs (searched recursively, newest match wins, cached 30 s). Null if nothing matches.
    /// </summary>
    public static string? FindInMapDirs(string exePath, string mapName)
    {
        var key = $"{exePath}|{mapName}";
        if (MapDirCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheTime && (hit.Path is null || File.Exists(hit.Path)))
            return hit.Path;
        string? found = null;
        try
        {
            var (_, configs) = HammerMcp.Core.Config.HammerGameConfig.ForExe(exePath);
            found = configs.Select(c => c.MapDir).OfType<string>().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(d => Directory.EnumerateFiles(d, Path.GetFileName(mapName), new EnumerationOptions
                {
                    RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true,
                }))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
        MapDirCache[key] = (found, DateTime.UtcNow);
        return found;
    }
}
