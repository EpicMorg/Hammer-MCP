namespace HammerMcp.Core.Maps;

/// <summary>Quick one-pass summary of a .vmf (no full parse): versioninfo, world keys, entity / brush counts.</summary>
public sealed record VmfSummary
{
    public int? MapVersion { get; init; }
    public int? EditorVersion { get; init; }
    public int? EditorBuild { get; init; }
    public int? FormatVersion { get; init; }
    public bool Prefab { get; init; }
    public string? SkyName { get; init; }
    public string? DetailMaterial { get; init; }
    public int Entities { get; init; }
    public int PointEntities { get; init; }
    public int BrushEntities { get; init; }
    public int WorldBrushes { get; init; }
    public int Brushes { get; init; }
    public int EntityClasses { get; init; }
    public int Visgroups { get; init; }
    public int HiddenObjects { get; init; }

    /// <summary>Counts per entity class, most frequent first.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Classes { get; init; } = [];

    public static VmfSummary Read(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    public static VmfSummary Parse(TextReader reader)
    {
        var stack = new List<string>();
        string? pending = null;
        var versionInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var world = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        int entities = 0, brushEntities = 0, worldBrushes = 0, brushes = 0, visgroups = 0, hidden = 0;
        var entityHasSolid = false;

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line == "{")
            {
                var name = pending ?? "?";
                stack.Add(name);
                pending = null;
                if (stack.Count == 1 && name == "entity")
                {
                    entities++;
                    entityHasSolid = false;
                }
                else if (stack.Count == 1 && name == "hidden")
                    hidden++;
                else if (name == "solid" && stack.Count == 2)
                {
                    brushes++;
                    if (stack[0] == "world")
                        worldBrushes++;
                    else if (stack[0] == "entity")
                        entityHasSolid = true;
                }
                else if (name == "visgroup")
                    visgroups++;
                continue;
            }
            if (line == "}")
            {
                if (stack.Count == 1 && stack[0] == "entity" && entityHasSolid)
                    brushEntities++;
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            if (line[0] == '"')
            {
                if (stack.Count != 1 || !TryKeyValue(line, out var key, out var value))
                    continue;
                switch (stack[0])
                {
                    case "versioninfo":
                        versionInfo[key] = value;
                        break;
                    case "world":
                        world[key] = value;
                        break;
                    case "entity" when key == "classname":
                        classes[value] = classes.GetValueOrDefault(value) + 1;
                        break;
                }
                continue;
            }
            pending = line;
        }

        return new VmfSummary
        {
            MapVersion = Int(versionInfo, "mapversion"),
            EditorVersion = Int(versionInfo, "editorversion"),
            EditorBuild = Int(versionInfo, "editorbuild"),
            FormatVersion = Int(versionInfo, "formatversion"),
            Prefab = versionInfo.GetValueOrDefault("prefab") == "1",
            SkyName = world.GetValueOrDefault("skyname"),
            DetailMaterial = world.GetValueOrDefault("detailmaterial"),
            Entities = entities,
            BrushEntities = brushEntities,
            PointEntities = entities - brushEntities,
            WorldBrushes = worldBrushes,
            Brushes = brushes,
            EntityClasses = classes.Count,
            Visgroups = visgroups,
            HiddenObjects = hidden,
            Classes = [.. classes.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal)],
        };
    }

    private static int? Int(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : null;

    /// <summary>Parses a line like  "key" "value".</summary>
    private static bool TryKeyValue(string line, out string key, out string value)
    {
        key = value = "";
        var k2 = line.IndexOf('"', 1);
        if (k2 < 0)
            return false;
        var v1 = line.IndexOf('"', k2 + 1);
        var v2 = line.LastIndexOf('"');
        if (v1 < 0 || v2 <= v1)
            return false;
        key = line[1..k2];
        value = line[(v1 + 1)..v2];
        return true;
    }
}
