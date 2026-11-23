using System.Globalization;
using System.Numerics;
using HammerMcp.Core.Assets;
using Sledge.Formats.Map.Formats;
using Sledge.Formats.Map.Objects;

namespace HammerMcp.Core.Maps;

/// <summary>Broad-phase preview for Source 1 props, using MDL view bounds and reconstructed VMF brush vertices.</summary>
public static class ModelClipping
{
    private sealed record Box(Vector3 Min, Vector3 Max)
    {
        public bool Overlaps(Box other) =>
            Min.X < other.Max.X && Max.X > other.Min.X && Min.Y < other.Max.Y && Max.Y > other.Min.Y
            && Min.Z < other.Max.Z && Max.Z > other.Min.Z;

        public static Box? From(IEnumerable<Vector3> points)
        {
            var lo = new Vector3(float.PositiveInfinity);
            var hi = new Vector3(float.NegativeInfinity);
            foreach (var point in points)
            {
                if (!Finite(point)) return null;
                lo = Vector3.Min(lo, point);
                hi = Vector3.Max(hi, point);
            }
            return lo.X < hi.X && lo.Y < hi.Y && lo.Z < hi.Z ? new Box(lo, hi) : null;
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static Box? LocalBounds(AssetResolver assets, string model)
    {
        if (!model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) || !model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            || model.Contains(':') || model.Split('/').Any(s => s is "" or "." or "..")) return null;
        var bytes = assets.Read(model)?.Data;
        if (bytes is null || bytes.Length < 152 || !bytes.AsSpan(0, 4).SequenceEqual("IDST"u8)
            || BitConverter.ToInt32(bytes, 4) is < 44 or > 49) return null;
        Vector3 At(int i) => new(BitConverter.ToSingle(bytes, i), BitConverter.ToSingle(bytes, i + 4), BitConverter.ToSingle(bytes, i + 8));
        Box? ReadBox(int offset)
        {
            var lo = At(offset);
            var hi = At(offset + 12);
            return Finite(lo) && Finite(hi) && lo.X < hi.X && lo.Y < hi.Y && lo.Z < hi.Z ? new(lo, hi) : null;
        }
        // Some shipped props leave the view box empty and only define hull bounds.
        return ReadBox(128) ?? ReadBox(104);
    }

    private static IEnumerable<Solid> Solids(MapObject parent)
    {
        foreach (var child in parent.Children)
            if (child is Solid solid) yield return solid;
            else foreach (var nested in Solids(child)) yield return nested;
    }

    public static object Check(string path, AssetResolver assets, int limit)
    {
        using var stream = File.OpenRead(path);
        var map = new HammerVmfFormat().Read(stream);
        var brushes = new List<(int Index, Box Bounds)>();
        var brushCount = 0;
        foreach (var solid in Solids(map.Worldspawn))
        {
            brushCount++;
            if (solid.Faces.All(f => f.Vertices.Count == 0)) solid.ComputeVertices(false);
            if (Box.From(solid.Faces.SelectMany(f => f.Vertices)) is {} box) brushes.Add((brushCount, box));
        }
        var cache = new Dictionary<string, Box?>(StringComparer.OrdinalIgnoreCase);
        var props = new List<(int Id, Box Bounds)>();
        var skipped = new List<int>();
        var candidates = new List<object>();
        long total = 0;
        foreach (var e in VmfEntities.Read(path).Where(e => e.ClassName.StartsWith("prop_", StringComparison.OrdinalIgnoreCase)))
        {
            var model = e.KeyValues.GetValueOrDefault("model")?.Replace('\\', '/') ?? "";
            if (!cache.TryGetValue(model, out var local)) cache[model] = local = LocalBounds(assets, model);
            var angles = VmfEntities.ParseVector(e.KeyValues.GetValueOrDefault("angles") ?? "0 0 0");
            if (local is null || e.Origin is not {} origin || angles is not {} a || !Finite(a)
                || !float.TryParse(e.KeyValues.GetValueOrDefault("modelscale") ?? "1", NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var scale) || !float.IsFinite(scale) || scale <= 0)
            {
                skipped.Add(e.Id);
                continue;
            }
            const float rad = MathF.PI / 180;
            var rotation = Matrix4x4.CreateRotationX(a.Z * rad) * Matrix4x4.CreateRotationY(a.X * rad) * Matrix4x4.CreateRotationZ(a.Y * rad);
            var bounds = Box.From(from x in new[] { local.Min.X, local.Max.X }
                                  from y in new[] { local.Min.Y, local.Max.Y }
                                  from z in new[] { local.Min.Z, local.Max.Z }
                                  select origin + Vector3.Transform(new Vector3(x, y, z) * scale, rotation));
            if (bounds is null) { skipped.Add(e.Id); continue; }
            foreach (var b in brushes.Where(b => bounds.Overlaps(b.Bounds)))
            {
                total++;
                if (candidates.Count < limit) candidates.Add(new { propId = e.Id, brushIndex = b.Index, brushMin = XYZ(b.Bounds.Min), brushMax = XYZ(b.Bounds.Max) });
            }
            foreach (var p in props.Where(p => bounds.Overlaps(p.Bounds)))
            {
                total++;
                if (candidates.Count < limit) candidates.Add(new { propId = e.Id, otherPropId = p.Id });
            }
            props.Add((e.Id, bounds));
        }
        return new { total, candidates, checkedProps = props.Count, skippedPropIds = skipped, checkedBrushes = brushes.Count, skippedBrushes = brushCount - brushes.Count };
    }

    private static float[] XYZ(Vector3 v) => [v.X, v.Y, v.Z];
}
