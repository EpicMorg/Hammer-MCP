using System.Text.RegularExpressions;
using HammerMcp.Core.Maps;

namespace HammerMcp.Core.Assets;

/// <summary>Content a map refers to: brush and displacement materials, models, overlays / decals, sprites, skybox.</summary>
public static partial class MapAssets
{
    public sealed record Reference(string Kind, string Path, int Uses, int? ExampleEntityId);

    /// <summary>All assets of a .vmf as game-relative paths (materials/x.vmt, models/x.mdl).</summary>
    public static List<Reference> Collect(string vmfPath)
    {
        var refs = new Dictionary<(string Kind, string Path), (int Uses, int? Id)>();
        void Add(string kind, string path, int? id)
        {
            var key = (kind, path.Replace('\\', '/').ToLowerInvariant());
            refs[key] = refs.TryGetValue(key, out var v) ? (v.Uses + 1, v.Id ?? id) : (1, id);
        }

        // brush faces: "material" "CONCRETE/CONCRETEFLOOR001A" directly inside side { } blocks
        var stack = new List<string>();
        string? pending = null;
        foreach (var raw in File.ReadLines(vmfPath))
        {
            var line = raw.Trim();
            if (line == "{")
            {
                stack.Add(pending ?? "?");
                pending = null;
            }
            else if (line == "}")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
            else if (line.StartsWith('"'))
            {
                if (stack.Count > 0 && stack[^1] == "side" && SideMaterialRegex().Match(line) is { Success: true } m)
                    Add("material", $"materials/{m.Groups[1].Value}.vmt", null);
            }
            else if (line.Length > 0)
                pending = line;
        }
        if (VmfSummary.Read(vmfPath).SkyName is { Length: > 0 } skyName)
        {
            foreach (var side in new[] { "rt", "lf", "bk", "ft", "up", "dn" })
                Add("skybox", $"materials/skybox/{skyName}{side}.vmt", null);
        }

        foreach (var e in VmfEntities.Read(vmfPath))
        {
            var kv = e.KeyValues;
            if (kv.TryGetValue("model", out var model) && model.Length > 0 && !model.StartsWith('*'))
            {
                if (model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                    Add("model", model, e.Id);
                else if (model.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
                    Add("sprite", model.StartsWith("materials/", StringComparison.OrdinalIgnoreCase) ? model : $"materials/{model}", e.Id);
            }
            if (e.ClassName.Equals("info_overlay", StringComparison.OrdinalIgnoreCase) && kv.TryGetValue("material", out var overlay))
                Add("overlay", $"materials/{overlay}.vmt", e.Id);
            if (e.ClassName.Equals("infodecal", StringComparison.OrdinalIgnoreCase) && kv.TryGetValue("texture", out var decal))
                Add("decal", $"materials/{decal}.vmt", e.Id);
        }
        return [.. refs.Select(r => new Reference(r.Key.Kind, r.Key.Path, r.Value.Uses, r.Value.Id)).OrderBy(r => r.Kind).ThenBy(r => r.Path)];
    }

    [GeneratedRegex(@"^\s*""material""\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SideMaterialRegex();
}
