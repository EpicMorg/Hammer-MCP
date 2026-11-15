using System.Numerics;
using Sledge.Formats.Map.Formats;
using Sledge.Formats.Map.Objects;
using Path = System.IO.Path;

namespace HammerMcp.Core.Maps;

/// <summary>
/// Entities of any map file Hammer edits. Source .vmf/.vmx/.vmm: our <see cref="VmfEntities"/> reader (keeps ids, hidden
/// entities and outputs, which Sledge drops). Source 2 .vmap: <see cref="VmapMap"/>. GoldSrc / Quake .rmf, .map, .jmf: Sledge.Formats.Map.
/// </summary>
public static class MapEntities
{
    public static List<VmfEntity> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".vmf" or ".vmx" or ".vmm" or ".vmf_autosave")
            return VmfEntities.Read(path);
        if (ext is ".vmap" || (ext is not (".rmf" or ".map" or ".jmf") && VmapMap.IsVmap(path)))
            return VmapMap.ReadCached(path).Entities;

        using var stream = File.OpenRead(path);
        MapFile map = ext switch
        {
            ".rmf" => new WorldcraftRmfFormat().Read(stream),
            ".map" => new QuakeMapFormat().Read(stream),
            ".jmf" => new JackhammerJmfFormat().Read(stream),
            _ => throw new NotSupportedException($"unknown map format {ext}"),
        };
        var result = new List<VmfEntity>();
        var id = 0;
        foreach (var e in Entities(map.Worldspawn))
        {
            var kv = new Dictionary<string, string>(e.Properties, StringComparer.OrdinalIgnoreCase);
            result.Add(new VmfEntity(++id, e.ClassName, VmfEntities.ParseVector(kv.GetValueOrDefault("origin")), kv.GetValueOrDefault("targetname"),
                Hidden: false, HasBrushes: e.Children.OfType<Solid>().Any(), kv));
        }
        return result;
    }

    /// <summary>Entities at any depth: .rmf / .jmf keep them inside groups (and visgroup folders).</summary>
    private static IEnumerable<Entity> Entities(MapObject parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is Entity e)
                yield return e;
            else
            {
                foreach (var nested in Entities(child))
                    yield return nested;
            }
        }
    }
}
