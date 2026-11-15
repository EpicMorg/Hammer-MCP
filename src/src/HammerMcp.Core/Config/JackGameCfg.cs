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
        var sections = Sections(lines);

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
            string Resolve(string f) => Path.IsPathRooted(f) ? f : Path.GetFullPath(Path.Combine(baseDir, f));
            result.Add(new HammerGameConfig(
                Get("Title") ?? name, Numbered("GameData").Select(Resolve).ToList(), gameDir,
                tools.TryGetValue("GameExe", out var gameExe) ? Path.GetDirectoryName(gameExe) : null,
                Get("MapSourceFileDirectory"),
                gameDir is null ? null : Path.Combine(gameDir, "maps"),
                tools, BaseGameDir: baseGame, GoldSrc: true, TextureWads: Numbered("Parcel").Select(Resolve).ToList()));
        }
        return result;
    }

    /// <summary>The [sections] of a QSettings ini with their key = value pairs (values unescaped); comments and blanks skipped.</summary>
    internal static List<(string Name, Dictionary<string, string> Values)> Sections(IEnumerable<string> lines)
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
        return sections;
    }

    private static string Windows(string path) => path.Replace('/', '\\');

    /// <summary>
    /// A QSettings ini value: quotes are dropped, escapes decoded - \\ \" \t \n \r and \xNNNN (QSettings writes every
    /// non-ASCII character that way, e.g. Cyrillic folder names: \x41f\x430\x43f\x43a\x430 = "Папка").
    /// </summary>
    internal static string Unescape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
                continue;
            if (c != '\\' || i + 1 >= value.Length)
            {
                sb.Append(c);
                continue;
            }
            var next = value[++i];
            switch (next)
            {
                case 't': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 'x':
                    var start = i + 1;
                    var end = start;
                    while (end < value.Length && end - start < 4 && Uri.IsHexDigit(value[end]))
                        end++;
                    if (end > start)
                    {
                        sb.Append((char)Convert.ToInt32(value[start..end], 16));
                        i = end - 1;
                    }
                    else
                        sb.Append('x');
                    break;
                default: sb.Append(next); break; // \\ \" and anything else: the character itself
            }
        }
        return sb.ToString();
    }
}
