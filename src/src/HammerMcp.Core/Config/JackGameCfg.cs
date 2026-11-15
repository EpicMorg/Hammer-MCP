namespace HammerMcp.Core.Config;

/// <summary>
/// VDKGameCfg.ini - game configurations of J.A.C.K. and Jackhammer, next to jack.exe / jackhammer.exe (Qt QSettings ini):
/// [Config0] Title, Enabled, MapProfile (vpHalfLife, vpQuake, ...), GameExecutable, GameBasedir, GameModdir,
/// MapSourceFileDirectory, MapTool0..N (compilers), GameData0..N (FGDs), Parcel0..N (texture WADs).
/// Values use forward slashes or doubled backslashes.
/// </summary>
public static class JackGameCfg
{
    public static List<HammerGameConfig> Load(string path) =>
        Parse(File.ReadAllLines(path), Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

    public static List<HammerGameConfig> Parse(IEnumerable<string> lines, string baseDir)
    {
        var sections = new List<(string Name, Dictionary<string, string> Values)>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//"))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                sections.Add((line[1..^1], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || sections.Count == 0)
                continue;
            sections[^1].Values[line[..eq].Trim()] = Unescape(line[(eq + 1)..].Trim());
        }

        var result = new List<HammerGameConfig>();
        foreach (var (name, v) in sections.Where(s => s.Name.StartsWith("Config", StringComparison.OrdinalIgnoreCase)))
        {
            if (v.TryGetValue("Enabled", out var enabled) && enabled.Equals("false", StringComparison.OrdinalIgnoreCase))
                continue;
            string? Get(string key) => v.TryGetValue(key, out var s) && s.Length > 0 ? Windows(s) : null;
            List<string> Numbered(string prefix) => v
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(kv.Key.AsSpan(prefix.Length), out _) && kv.Value.Length > 0)
                .OrderBy(kv => int.Parse(kv.Key.AsSpan(prefix.Length)))
                .Select(kv => Windows(kv.Value))
                .ToList();

            var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Get("GameExecutable") is { } exe)
                tools["GameExe"] = exe;
            foreach (var tool in Numbered("MapTool"))
            {
                var file = Path.GetFileNameWithoutExtension(tool).ToLowerInvariant();
                var key = file.Contains("csg") ? "CSG" : file.Contains("bsp") ? "BSP" : file.Contains("vis") ? "Vis"
                    : file.Contains("rad") || file.Contains("light") ? "Light" : null;
                if (key is not null)
                    tools.TryAdd(key, tool);
            }
            var modDir = Get("GameModdir");
            var baseGame = Get("GameBasedir");
            var gameDir = modDir ?? baseGame;
            var fgds = Numbered("GameData").Select(f => Path.IsPathRooted(f) ? f : Path.GetFullPath(Path.Combine(baseDir, f))).ToList();
            result.Add(new HammerGameConfig(
                Get("Title") ?? name, fgds, gameDir,
                tools.TryGetValue("GameExe", out var gameExe) ? Path.GetDirectoryName(gameExe) : null,
                Get("MapSourceFileDirectory"),
                gameDir is null ? null : Path.Combine(gameDir, "maps"),
                tools, BaseGameDir: baseGame, GoldSrc: true, TextureWads: Numbered("Parcel")));
        }
        return result;
    }

    private static string Windows(string path) => path.Replace('/', '\\');

    /// <summary>QSettings escapes: \\ → \, surrounding quotes removed.</summary>
    private static string Unescape(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value.Replace(@"\\", @"\");
    }
}
