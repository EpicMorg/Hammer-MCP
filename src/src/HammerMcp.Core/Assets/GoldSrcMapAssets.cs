using Sledge.Formats.Map.Formats;
using Sledge.Formats.Map.Objects;
using Path = System.IO.Path;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Content a GoldSrc / Quake map (.rmf, .map, .jmf) refers to: face textures and infodecal decals (names in WADs),
/// models (.mdl), sprites (.spr) and sounds (.wav) in any keyvalue, the sky (gfx/env/&lt;skyname&gt;rt.tga ...), and the
/// WADs listed in worldspawn's "wad" key.
/// </summary>
public static class GoldSrcMapAssets
{
    /// <summary>Texture names the compilers handle themselves (ZHLT / VHLT tool textures): never looked up.</summary>
    private static readonly HashSet<string> ToolTextures = new(StringComparer.OrdinalIgnoreCase)
    {
        "NULL", "CLIP", "ORIGIN", "SKIP", "HINT", "BEVEL", "SOLIDHINT", "BOUNDINGBOX", "CLIPBEVEL", "CLIPBEVELBRUSH",
        "CLIPHULL1", "CLIPHULL2", "CLIPHULL3", "CONTENTWATER", "CONTENTEMPTY", "CONTENTSOLID", "CONTENTSKY", "SPLITFACE",
    };

    public sealed record Result(List<MapAssets.Reference> References, IReadOnlyList<string> Wads);

    public static bool IsGoldSrcMap(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".rmf" or ".map" or ".jmf";

    public static Result Collect(string mapPath)
    {
        using var stream = File.OpenRead(mapPath);
        MapFile map = Path.GetExtension(mapPath).ToLowerInvariant() switch
        {
            ".rmf" => new WorldcraftRmfFormat().Read(stream),
            ".map" => new QuakeMapFormat().Read(stream),
            ".jmf" => new JackhammerJmfFormat().Read(stream),
            var ext => throw new NotSupportedException($"not a GoldSrc map: {ext}"),
        };
        var refs = new Dictionary<(string Kind, string Path), (int Uses, int? Id)>();
        void Add(string kind, string path, int? id)
        {
            var key = (kind, kind is "texture" or "decal" ? path : path.Replace('\\', '/').ToLowerInvariant());
            refs[key] = refs.TryGetValue(key, out var v) ? (v.Uses + 1, v.Id ?? id) : (1, id);
        }

        var entityId = 0;
        void Walk(MapObject obj)
        {
            if (obj is Solid solid)
            {
                foreach (var face in solid.Faces)
                {
                    if (!string.IsNullOrEmpty(face.TextureName) && !ToolTextures.Contains(face.TextureName))
                        Add("texture", face.TextureName, null);
                }
            }
            else if (obj is Entity entity and not Worldspawn)
            {
                var id = ++entityId;
                foreach (var (key, value) in entity.Properties)
                    AddKeyValue(entity.ClassName, key, value, id);
            }
            foreach (var child in obj.Children)
                Walk(child);
        }

        void AddKeyValue(string className, string key, string value, int? id)
        {
            if (value.Length == 0 || value.StartsWith('*'))
                return;
            if (className.Equals("infodecal", StringComparison.OrdinalIgnoreCase) && key.Equals("texture", StringComparison.OrdinalIgnoreCase))
                Add("decal", value, id);
            else if (value.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                Add("model", value, id);
            else if (value.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                Add("sprite", value, id);
            else if (value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && !value.StartsWith('!'))
                Add("sound", "sound/" + value.TrimStart('/', '\\'), id);
        }

        Walk(map.Worldspawn);
        var world = map.Worldspawn.Properties;
        if (world.TryGetValue("skyname", out var sky) && sky.Length > 0)
        {
            foreach (var side in new[] { "rt", "lf", "bk", "ft", "up", "dn" })
                Add("skybox", $"gfx/env/{sky}{side}.tga", null);
        }
        var wads = world.TryGetValue("wad", out var wadList)
            ? wadList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];
        var list = refs.Select(r => new MapAssets.Reference(r.Key.Kind, r.Key.Path, r.Value.Uses, r.Value.Id))
            .OrderBy(r => r.Kind).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return new Result(list, wads);
    }
}
