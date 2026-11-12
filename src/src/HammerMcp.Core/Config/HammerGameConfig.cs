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

    private static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\') + "\\";

    private static KVObject? Child(KVObject obj, string name) =>
        obj.Children.FirstOrDefault(c => c.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
