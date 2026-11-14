using System.Text.RegularExpressions;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Finds content by name in a game's search paths (loose folders and VPKs): models, materials, textures, sounds,
/// sprites, particles. Source 2 packs hold compiled files (x.vmdl_c); they are reported as Hammer writes them (x.vmdl).
/// </summary>
public static class AssetSearch
{
    /// <summary>A kind of content: where it lives, its extensions, and how a keyvalue refers to it.</summary>
    /// <param name="StripRoot">Keyvalues name it without the root folder (Source 1 materials/, sound/).</param>
    /// <param name="StripExtension">Keyvalues name it without the extension (Source 1 brush / decal / overlay materials).</param>
    public sealed record Kind(string Name, string Root, string[] Extensions, bool StripRoot = false, bool StripExtension = false);

    public static readonly IReadOnlyList<Kind> Source1 =
    [
        new("model", "models", [".mdl"]),
        new("material", "materials", [".vmt"], StripRoot: true, StripExtension: true),
        new("texture", "materials", [".vtf"]),
        new("sound", "sound", [".wav", ".mp3", ".ogg"], StripRoot: true),
        new("sprite", "materials/sprites", [".vmt"]),
        new("particle", "particles", [".pcf"]),
    ];

    public static readonly IReadOnlyList<Kind> Source2 =
    [
        new("model", "models", [".vmdl"]),
        new("material", "materials", [".vmat"]),
        new("texture", "materials", [".vtex"]),
        new("sound", "sounds", [".vsnd"]),
        new("particle", "particles", [".vpcf"]),
    ];

    /// <summary>One file found: its path, what to write in a keyvalue, and every source that has it (the first one wins).</summary>
    public sealed record Hit(string Kind, string Path, string KeyValue, IReadOnlyList<string> Sources);

    public sealed record Result(int Total, IReadOnlyList<Hit> Hits);

    /// <summary>
    /// <paramref name="query"/>: a part of the path ("cabinet") or a wildcard that matches the end of the path from any
    /// folder on ("props_2fort/*locker*", "ambient/wind*", "*.vmt").
    /// <paramref name="kind"/>: one of <paramref name="kinds"/> by name, or null / "any" for all of them.
    /// </summary>
    public static Result Search(AssetResolver assets, IReadOnlyList<Kind> kinds, string query, string? kind, int limit)
    {
        var wanted = kind is null or "" or "any"
            ? kinds
            : kinds.Where(k => k.Name.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
        var match = Matcher(query);
        var found = new Dictionary<string, (Kind Kind, string Path, List<string> Sources)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in wanted.GroupBy(k => k.Root))
        {
            foreach (var (file, source) in assets.List(root.Key))
            {
                var path = file.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? file[..^2] : file; // Source 2 compiled
                var k = root.FirstOrDefault(k => k.Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
                if (k is null || !match(path))
                    continue;
                // a sprite is also a material: report it once, as the more specific kind
                var key = path;
                if (found.TryGetValue(key, out var hit))
                {
                    if (!hit.Sources.Contains(source))
                        hit.Sources.Add(source);
                }
                else
                    found[key] = (k, path, [source]);
            }
        }
        // sprites live under materials/: the more specific kind wins when both were searched
        foreach (var sprite in wanted.Where(k => k.Name == "sprite"))
        {
            foreach (var key in found.Keys.ToList())
            {
                var v = found[key];
                if (v.Kind.Name == "material" && key.StartsWith(sprite.Root + "/", StringComparison.OrdinalIgnoreCase))
                    found[key] = (sprite, v.Path, v.Sources);
            }
        }
        var hits = found.Values
            .OrderBy(v => v.Path.Length).ThenBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(v => new Hit(v.Kind.Name, v.Path, KeyValue(v.Kind, v.Path), v.Sources))
            .ToList();
        return new Result(found.Count, hits);
    }

    /// <summary>How a keyvalue names the file (Source 1: material "tools/toolsnodraw", sound "ambient/wind.wav").</summary>
    public static string KeyValue(Kind kind, string path)
    {
        var value = path;
        if (kind.StripRoot && value.StartsWith(kind.Root + "/", StringComparison.OrdinalIgnoreCase))
            value = value[(kind.Root.Length + 1)..];
        if (kind.StripExtension && Path.HasExtension(value))
            value = value[..^Path.GetExtension(value).Length];
        // env_sprite's model keyvalue is "sprites/x.vmt", relative to materials/
        if (kind.Name == "sprite" && value.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
            value = value["materials/".Length..];
        return value;
    }

    private static Func<string, bool> Matcher(string query)
    {
        var q = query.Trim().Replace('\\', '/');
        if (q.IndexOfAny(['*', '?']) < 0)
            return path => path.Contains(q, StringComparison.OrdinalIgnoreCase);
        // anchored at the end and at a folder boundary: "ambient/wind*" finds sound/ambient/wind01.wav
        var regex = new Regex("^(.*/)?" + Regex.Escape(q.TrimStart('/')).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return path => regex.IsMatch(path);
    }
}
