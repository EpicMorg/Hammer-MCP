using System.Globalization;
using System.Numerics;

namespace HammerMcp.Core.Maps;

/// <summary>One entity of a map file: keyvalues and outputs (connections), plus what addressing needs.</summary>
public sealed record VmfEntity(int Id, string ClassName, Vector3? Origin, string? TargetName, bool Hidden, bool HasBrushes,
    IReadOnlyDictionary<string, string> KeyValues)
{
    /// <summary>Outputs from the entity's "connections" block (Source maps only).</summary>
    public IReadOnlyList<VmfConnection> Connections { get; init; } = [];

    /// <summary>Keyvalue / output lines with extra double quotes inside the value (e.g. echo "text"): Hammer and vbsp split them.</summary>
    public IReadOnlyList<string> BrokenLines { get; init; } = [];
}

/// <summary>An output: OnTrigger -> target.Input(parameter) after delay, fired TimesToFire times (-1 = always).</summary>
public sealed record VmfConnection(string Output, string Target, string Input, string Parameter, float Delay, int TimesToFire)
{
    /// <summary>Parses the value of a connections line: "target,input,param,delay,times" (older Hammer) or the same with ESC (0x1B) separators (newer).</summary>
    public static VmfConnection? Parse(string output, string value)
    {
        var parts = value.Split(value.Contains('\u001b') ? '\u001b' : ',');
        if (parts.Length < 5)
            return null;
        float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay);
        int.TryParse(parts[4], out var times);
        return new VmfConnection(output, parts[0], parts[1], parts[2], delay, times);
    }
}

/// <summary>Reads all entities of a .vmf, including those inside hidden blocks (Hammer's shell channel sees them too).</summary>
public static class VmfEntities
{
    /// <summary>Hammer prints origins with up to 6 decimals; treat closer points as the same address.</summary>
    public const float OriginTolerance = 0.01f;

    public static List<VmfEntity> Read(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    public static List<VmfEntity> Parse(TextReader reader)
    {
        var result = new List<VmfEntity>();
        var stack = new List<string>();
        string? pending = null;
        Dictionary<string, string>? kv = null;
        List<VmfConnection>? connections = null;
        List<string>? broken = null;
        var entityDepth = -1;
        var hasBrushes = false;

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line == "{")
            {
                var name = pending ?? "?";
                pending = null;
                stack.Add(name);
                if (name == "entity" && entityDepth < 0 && (stack.Count == 1 || (stack.Count == 2 && stack[0] == "hidden")))
                {
                    entityDepth = stack.Count;
                    kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    connections = [];
                    broken = [];
                    hasBrushes = false;
                }
                else if (name == "solid" && entityDepth > 0 && stack.Count == entityDepth + 1)
                    hasBrushes = true;
                continue;
            }
            if (line == "}")
            {
                if (entityDepth > 0 && stack.Count == entityDepth && kv is not null)
                {
                    result.Add(new VmfEntity(
                        int.TryParse(kv.GetValueOrDefault("id"), out var id) ? id : 0,
                        kv.GetValueOrDefault("classname") ?? "?",
                        ParseVector(kv.GetValueOrDefault("origin")),
                        kv.GetValueOrDefault("targetname"),
                        Hidden: entityDepth == 2,
                        hasBrushes,
                        kv) { Connections = connections ?? [], BrokenLines = broken ?? [] });
                    entityDepth = -1;
                    kv = null;
                }
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            if (line[0] == '"')
            {
                if (kv is not null && TryKeyValue(line, out var key, out var value))
                {
                    if (value.Contains('"'))
                        broken!.Add(line);
                    if (stack.Count == entityDepth)
                        kv[key] = value;
                    else if (stack.Count == entityDepth + 1 && stack[^1] == "connections" && VmfConnection.Parse(key, value) is { } c)
                        connections!.Add(c);
                }
                continue;
            }
            pending = line;
        }
        return result;
    }

    /// <summary>Entities of <paramref name="className"/> at <paramref name="origin"/> (within <see cref="OriginTolerance"/>).</summary>
    public static IEnumerable<VmfEntity> At(this IEnumerable<VmfEntity> entities, string className, Vector3 origin) =>
        entities.Where(e => string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase)
                            && e.Origin is { } o && SameOrigin(o, origin));

    public static bool SameOrigin(Vector3 a, Vector3 b) =>
        MathF.Abs(a.X - b.X) <= OriginTolerance && MathF.Abs(a.Y - b.Y) <= OriginTolerance && MathF.Abs(a.Z - b.Z) <= OriginTolerance;

    public static Vector3? ParseVector(string? s)
    {
        if (s is null)
            return null;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;
        var ok = float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                 & float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                 & float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z);
        return ok ? new Vector3(x, y, z) : null;
    }

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
