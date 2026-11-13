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
}
