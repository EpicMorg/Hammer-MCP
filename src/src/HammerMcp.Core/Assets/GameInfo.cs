using ValveKeyValue;

namespace HammerMcp.Core.Assets;

/// <summary>A game's or mod's gameinfo.txt: title, app id and the search paths the engine and tools use to find content.</summary>
public sealed partial record GameInfo(string Path, string? Game, int? SteamAppId, IReadOnlyList<GameInfo.SearchPath> SearchPaths)
{
    /// <summary>One "SearchPaths" line: kinds like "game+mod", the raw value and the resolved file system locations.</summary>
    public sealed record SearchPath(string Kinds, string Value, IReadOnlyList<string> Locations)
    {
        public bool IsGame => Kinds.Split('+').Any(k => k.Equals("game", StringComparison.OrdinalIgnoreCase));
    }

    /// <param name="engineRoot">
    /// Folder that |all_source_engine_paths| points to (the game install with hl2/, platform/... - Hammer's GameExeDir).
    /// Defaults to the parent of the gameinfo folder.
    /// </param>
    /// <param name="appRoot">Install folder of a Steam app, for |appid_NNN| paths (mods mounting another game's content).</param>
    public static GameInfo Load(string path, string? engineRoot = null, Func<int, string?>? appRoot = null)
    {
        using var stream = File.OpenRead(path);
        var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        var gameDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        engineRoot ??= System.IO.Path.GetDirectoryName(gameDir) ?? gameDir;

        var fs = Child(doc.Root, "FileSystem");
        var paths = new List<SearchPath>();
        foreach (var (kinds, value) in Child(fs, "SearchPaths")?.Children ?? [])
        {
            if (value.IsCollection)
                continue;
            var raw = value.ToString() ?? "";
            paths.Add(new SearchPath(kinds, raw, Resolve(raw, gameDir, engineRoot, appRoot)));
        }
        int? appId = int.TryParse(Child(fs, "SteamAppId")?.ToString(), out var id) ? id : null;
        return new GameInfo(path, Child(doc.Root, "game")?.ToString(), appId, paths);
    }

    /// <summary>
    /// Expands |gameinfo_path|, |all_source_engine_paths|, |appid_NNN| (install folder of another Steam app) and a trailing
    /// "/*" (every subfolder and vpk, e.g. custom/*).
    /// </summary>
    public static List<string> Resolve(string value, string gameDir, string engineRoot, Func<int, string?>? appRoot = null)
    {
        var v = value.Replace('/', System.IO.Path.DirectorySeparatorChar).Replace('\\', System.IO.Path.DirectorySeparatorChar);
        string full;
        if (AppIdRegex().Match(v) is { Success: true } app)
        {
            var root = appRoot?.Invoke(int.Parse(app.Groups[1].Value));
            if (root is null)
                return [];
            full = System.IO.Path.Combine(root, v[app.Length..]);
        }
        else if (v.StartsWith("|gameinfo_path|", StringComparison.OrdinalIgnoreCase))
            full = System.IO.Path.Combine(gameDir, v["|gameinfo_path|".Length..]);
        else if (v.StartsWith("|all_source_engine_paths|", StringComparison.OrdinalIgnoreCase))
            full = System.IO.Path.Combine(engineRoot, v["|all_source_engine_paths|".Length..]);
        else
            full = System.IO.Path.IsPathRooted(v) ? v : System.IO.Path.Combine(engineRoot, v);

        if (!full.EndsWith('*'))
            return [System.IO.Path.GetFullPath(full.TrimEnd('.'))];
        var dir = System.IO.Path.GetDirectoryName(full)!;
        if (!Directory.Exists(dir))
            return [];
        return [.. Directory.EnumerateDirectories(dir).Concat(Directory.EnumerateFiles(dir, "*_dir.vpk")).OrderBy(x => x)];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\|appid_(\d+)\|", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AppIdRegex();

    private static KVObject? Child(KVObject? obj, string name) =>
        obj?.Children.FirstOrDefault(c => c.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
