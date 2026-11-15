using System.Text.RegularExpressions;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Finds content by name in a game's search paths (loose folders and VPKs): models, materials, textures, sounds,
/// sprites, particles. Source 2 packs hold compiled files (x.vmdl_c); they are reported as Hammer writes them (x.vmdl).
/// GoldSrc: loose files and texture names from WADs.
/// </summary>
public static class AssetSearch
{
    /// <summary>A kind of content: where it lives, its extensions, and how a keyvalue refers to it.</summary>
    /// <param name="StripRoot">Keyvalues name it without the root folder (Source 1 materials/, sound/).</param>
    /// <param name="StripExtension">Keyvalues name it without the extension (Source 1 brush / decal / overlay materials).</param>
    /// <param name="Wad">GoldSrc: a texture name inside a WAD, not a file.</param>
    public sealed record Kind(string Name, string Root, string[] Extensions, bool StripRoot = false, bool StripExtension = false, bool Wad = false);

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

    /// <summary>
    /// GoldSrc: loose models / sprites / sounds in the mod and its base game, textures and decals by name from WADs.
    /// Keyvalues name models and sprites with the folder (models/scientist.mdl), sounds without sound/.
    /// </summary>
    public static readonly IReadOnlyList<Kind> GoldSrc =
    [
        new("model", "models", [".mdl"]),
        new("sprite", "sprites", [".spr"]),
        new("sound", "sound", [".wav"], StripRoot: true),
        new("texture", "", [], Wad: true),
        new("decal", "", [], Wad: true),
    ];

    /// <summary>One file found: its path, what to write in a keyvalue, and every source that has it (the first one wins).</summary>
    public sealed record Hit(string Kind, string Path, string KeyValue, IReadOnlyList<string> Sources);

    public sealed record Result(int Total, IReadOnlyList<Hit> Hits);

    /// <summary>
    /// <paramref name="query"/>: a part of the path ("cabinet") or a wildcard that matches the end of the path from any
    /// folder on ("props_2fort/*locker*", "ambient/wind*", "*.vmt").
    /// <paramref name="kind"/>: one of <paramref name="kinds"/> by name, or null / "any" for all of them.
    /// </summary>
    public static Result Search(AssetResolver assets, IReadOnlyList<Kind> kinds, string query, string? kind, int limit) =>
        Collect(Files(assets, Wanted(kinds, kind)), Wanted(kinds, kind), query, limit);

    /// <summary>
    /// GoldSrc search: files as in <see cref="Search"/> (model texture / sequence-group files like scientistt.mdl and
    /// scientist01.mdl are not offered), plus mip textures from <paramref name="wads"/> - decals.wad gives "decal", the
    /// rest "texture"; a name in several WADs is reported once, from the first WAD.
    /// </summary>
    public static Result SearchGoldSrc(AssetResolver files, IReadOnlyList<WadDirectory> wads, string query, string? kind, int limit)
    {
        var wanted = Wanted(GoldSrc, kind);
        var found = Files(files, wanted).ToList();
        var models = found.Where(f => f.Kind.Name == "model").Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        found.RemoveAll(f => f.Kind.Name == "model" && IsModelPart(f.Path, models));
        foreach (var wad in wads)
        {
            var decals = Path.GetFileName(wad.Path).Equals("decals.wad", StringComparison.OrdinalIgnoreCase);
            var k = wanted.FirstOrDefault(k => k.Name == (decals ? "decal" : "texture"));
            if (k is null)
                continue;
            foreach (var texture in wad.Textures)
                found.Add((k, texture, wad.Path));
        }
        return Collect(found, wanted, query, limit);
    }

    /// <summary>scientistt.mdl (textures) and scientist01.mdl (sequence group) belong to scientist.mdl.</summary>
    private static bool IsModelPart(string path, HashSet<string> models)
    {
        var stem = path[..^4];
        if (stem.EndsWith('t') || stem.EndsWith('T'))
            return models.Contains(stem[..^1] + ".mdl");
        return stem.Length > 2 && char.IsAsciiDigit(stem[^1]) && char.IsAsciiDigit(stem[^2]) && models.Contains(stem[..^2] + ".mdl");
    }

    private static IReadOnlyList<Kind> Wanted(IReadOnlyList<Kind> kinds, string? kind) =>
        kind is null or "" or "any" ? kinds : kinds.Where(k => k.Name.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Files of the wanted kinds, in source order (Source 2 compiled x.vmdl_c reported as x.vmdl).</summary>
    private static IEnumerable<(Kind Kind, string Path, string Source)> Files(AssetResolver assets, IReadOnlyList<Kind> wanted)
    {
        foreach (var root in wanted.Where(k => !k.Wad).GroupBy(k => k.Root))
        {
            foreach (var (file, source) in assets.List(root.Key))
            {
                var path = file.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? file[..^2] : file;
                var k = root.FirstOrDefault(k => k.Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
                if (k is not null)
                    yield return (k, path, source);
            }
        }
    }

    private static Result Collect(IEnumerable<(Kind Kind, string Path, string Source)> candidates, IReadOnlyList<Kind> wanted, string query, int limit)
    {
        var match = Matcher(query);
        var found = new Dictionary<string, (Kind Kind, string Path, List<string> Sources)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, path, source) in candidates)
        {
            if (!match(path) && !(k.Wad && WadStem(path) is { } stem && match(stem)))
                continue;
            // a WAD texture and a file can not share a key: files have a folder and an extension
            if (found.TryGetValue(path, out var hit))
            {
                if (!hit.Sources.Contains(source))
                    hit.Sources.Add(source);
            }
            else
                found[path] = (k, path, [source]);
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

    /// <summary>A WAD name without its GoldSrc prefix: {transparent, !water, +0animated / -0random toggle, ~light; null if none.</summary>
    private static string? WadStem(string name)
    {
        if (name.Length > 2 && name[0] is '+' or '-' && char.IsAsciiLetterOrDigit(name[1]))
            return name[2..];
        return name.Length > 1 && name[0] is '{' or '!' or '~' ? name[1..] : null;
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
