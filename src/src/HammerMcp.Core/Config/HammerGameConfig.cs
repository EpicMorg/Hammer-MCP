using ValveKeyValue;

namespace HammerMcp.Core.Config;

/// <summary>One game configuration from Hammer's GameConfig.txt (stock) or hammerplusplus_gameconfig.txt (Hammer++).</summary>
public sealed record HammerGameConfig(
    string Name,
    IReadOnlyList<string> FgdFiles,
    string? GameDir,
    string? GameExeDir,
    string? MapDir,
    string? BspDir,
    IReadOnlyDictionary<string, string> Tools)
{
    /// <summary>Config files next to a Hammer exe: Hammer++ first, then stock (bin/ or bin/x64/).</summary>
    public static IEnumerable<string> CandidateFiles(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath) ?? ".";
        yield return Path.Combine(dir, "hammerplusplus", "hammerplusplus_gameconfig.txt");
        yield return Path.Combine(dir, "GameConfig.txt");
        yield return Path.Combine(dir, "..", "GameConfig.txt");
        // Strata: <game>\bin\win64\hammer.exe, config in <game>\hammer\cfg\gameconfig.txt
        yield return Path.Combine(dir, "..", "..", "hammer", "cfg", "gameconfig.txt");
    }

    /// <summary>All game configurations of the Hammer at <paramref name="exePath"/>, from the first config file that exists.</summary>
    public static (string? File, List<HammerGameConfig> Configs) ForExe(string exePath)
    {
        foreach (var file in CandidateFiles(exePath))
        {
            if (File.Exists(file))
                return (Path.GetFullPath(file), Load(file));
        }
        return (null, []);
    }

    /// <summary>
    /// Source 2 has no GameConfig.txt: Hammer lives in game/bin/win64/tools/hammer.dll and each mod is game/&lt;mod&gt;/ with
    /// &lt;mod&gt;.fgd (core FGDs come in through @include from game/core/). Maps live under content/&lt;mod&gt;/ or
    /// content/&lt;mod&gt;_addons/&lt;addon&gt;/. One config per mod that has its FGD; empty if this is not a Source 2 install.
    /// </summary>
    public static List<HammerGameConfig> Source2(string exePath)
    {
        var bin = Path.GetDirectoryName(exePath) ?? ".";
        if (!File.Exists(Path.Combine(bin, "tools", "hammer.dll")))
            return [];
        var game = Path.GetFullPath(Path.Combine(bin, "..", ".."));
        var content = Path.GetFullPath(Path.Combine(game, "..", "content"));
        var result = new List<HammerGameConfig>();
        if (!Directory.Exists(game))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(game).Order(StringComparer.OrdinalIgnoreCase))
        {
            var mod = Path.GetFileName(dir);
            var fgd = Path.Combine(dir, mod + ".fgd");
            if (mod.Equals("core", StringComparison.OrdinalIgnoreCase) || mod.Equals("bin", StringComparison.OrdinalIgnoreCase) || !File.Exists(fgd))
                continue;
            result.Add(new HammerGameConfig(mod, [fgd], dir, game, Path.Combine(content, mod), Path.Combine(dir, "maps"),
                new Dictionary<string, string>()));
        }
        return result;
    }

    /// <summary>Source 2 mod of a map path: the folder after "content" (or "game"), "_addons" stripped (csgo_addons → csgo).</summary>
    public static string? Source2ModOf(string? mapPath)
    {
        if (mapPath is null)
            return null;
        var parts = mapPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("content", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                var mod = parts[i + 1];
                return mod.EndsWith("_addons", StringComparison.OrdinalIgnoreCase) ? mod[..^"_addons".Length] : mod;
            }
        }
        return null;
    }

    public static List<HammerGameConfig> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static List<HammerGameConfig> Parse(Stream stream)
    {
        var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        // "Configs" { "Games" { "<name>" { "GameDir" ... "Hammer" { "GameData0" ... "BSP" ... } } } }
        var games = string.Equals(doc.Name, "Games", StringComparison.OrdinalIgnoreCase) ? doc.Root : Child(doc.Root, "Games");
        var result = new List<HammerGameConfig>();
        foreach (var (gameName, game) in games?.Children ?? [])
        {
            if (!game.IsCollection)
                continue;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in Child(game, "Hammer")?.Children ?? [])
            {
                if (!value.IsCollection)
                    values[key] = value.ToString() ?? "";
            }
            var fgds = values.Where(kv => kv.Key.StartsWith("GameData", StringComparison.OrdinalIgnoreCase) && kv.Value.Length > 0)
                .OrderBy(kv => int.TryParse(kv.Key["GameData".Length..], out var n) ? n : int.MaxValue)
                .Select(kv => kv.Value)
                .ToList();
            var tools = values.Where(kv => kv.Key is "BSP" or "Vis" or "Light" or "GameExe" or "PrefabDir" or "BSPDir" or "MapDir" or "CordonTexture")
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            var gameDir = Child(game, "GameDir") is { IsCollection: false } g ? g.ToString() : null;
            result.Add(new HammerGameConfig(gameName, fgds, gameDir, values.GetValueOrDefault("GameExeDir"),
                values.GetValueOrDefault("MapDir"), values.GetValueOrDefault("BSPDir"), tools));
        }
        return result;
    }

    /// <summary>The config whose MapDir (or GameDir) contains the map; the only config if there is one; else null.</summary>
    public static HammerGameConfig? ForMap(IReadOnlyList<HammerGameConfig> configs, string? mapPath)
    {
        if (configs.Count == 1)
            return configs[0];
        if (mapPath is null)
            return null;
        var full = Normalize(mapPath);
        return configs.FirstOrDefault(c => c.MapDir is { Length: > 0 } d && full.StartsWith(Normalize(d), StringComparison.OrdinalIgnoreCase))
               ?? configs.FirstOrDefault(c => c.GameDir is { Length: > 0 } g && full.StartsWith(Normalize(Path.GetDirectoryName(g) ?? g), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The config of the game this Hammer ships with, for maps outside every MapDir: the only config whose GameDir is
    /// inside the Hammer's install (the folder above bin/) and has a gameinfo.txt. In the TF2 install that is "tf" -
    /// its "hl2" folder is shared content without a gameinfo.txt. Null if none or several qualify.
    /// </summary>
    public static HammerGameConfig? ForHammer(IReadOnlyList<HammerGameConfig> configs, string exePath)
    {
        var bin = Path.GetDirectoryName(exePath);
        if (bin is null)
            return null;
        // bin/ or bin/x64/
        var root = Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(bin) : Path.GetDirectoryName(Path.GetDirectoryName(bin));
        if (root is null)
            return null;
        var own = configs.Where(c => c.GameDir is { Length: > 0 } g
                                     && Normalize(g).StartsWith(Normalize(root), StringComparison.OrdinalIgnoreCase)
                                     && File.Exists(Path.Combine(g, "gameinfo.txt"))).ToList();
        return own.Count == 1 ? own[0] : null;
    }

    /// <summary>Backslashes, ".." / "." collapsed (Strata writes "...\\bin\\win64\\..\\..\\sdk_content\\maps"), trailing backslash.</summary>
    private static string Normalize(string p)
    {
        var parts = new List<string>();
        foreach (var part in p.Replace('/', '\\').Split('\\'))
        {
            if (part == "." || (part.Length == 0 && parts.Count > 0))
                continue;
            if (part == ".." && parts.Count > 1 && parts[^1] != "..")
                parts.RemoveAt(parts.Count - 1);
            else
                parts.Add(part);
        }
        return string.Join('\\', parts).TrimEnd('\\') + "\\";
    }

    private static KVObject? Child(KVObject obj, string name) =>
        obj.Children.FirstOrDefault(c => c.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
