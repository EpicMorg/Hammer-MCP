using System.Globalization;
using System.Numerics;

namespace HammerMcp.Core.Maps;

internal static class DisplacementSupport
{
    // Source's full-resolution tessellation fans each 2-by-2 block around its center.
    public static float Height(string data, (BrushPoint Min, BrushPoint Max) bounds, Vector2 min, Vector2 max)
    {
        var values = new Dictionary<(string Block, string Key), string>();
        string block = "", pending = "";
        foreach (var line in data.Split('\n'))
        {
            var s = line.Trim();
            if (s == "{") block = pending;
            else if (s == "}") block = "";
            else if (s.StartsWith('"') && VmfEntities.TryKeyValue(s, out var key, out var value))
            {
                if (!values.TryAdd((block, key), value)) throw Invalid();
            }
            else if (s.Length > 0) pending = s;
        }
        if (!int.TryParse(values.GetValueOrDefault(("", "power")), out int power) || power is < 2 or > 4)
            throw Invalid();
        int cells = 1 << power, side = cells + 1;
        var start = Numbers(values.GetValueOrDefault(("", "startposition"))?.Trim('[', ']'), 3);
        var elevation = Numbers(values.GetValueOrDefault(("", "elevation")) ?? "0", 1)[0];
        var lo = new Vector2(bounds.Min.X, bounds.Min.Y);
        var hi = new Vector2(bounds.Max.X, bounds.Max.Y);
        Vector2[] corners = [lo, new(lo.X, hi.Y), hi, new(hi.X, lo.Y)];
        int corner = Array.IndexOf(corners, new Vector2(start[0], start[1]));
        if (corner < 0 || start[2] != bounds.Max.Z) throw Invalid();
        var row = (corners[(corner + 1) % 4] - corners[corner]) / cells;
        var col = (corners[(corner + 3) % 4] - corners[corner]) / cells;
        var vertices = new Vector3[side * side];
        for (int i = 0; i < side; i++)
        {
            var distances = Numbers(values.GetValueOrDefault(("distances", $"row{i}")), side);
            var normals = Numbers(values.GetValueOrDefault(("normals", $"row{i}")), side * 3);
            var offsets = values.TryGetValue(("offsets", $"row{i}"), out var text) ? Numbers(text, side * 3) : null;
            for (int j = 0; j < side; j++)
            {
                int k = j * 3;
                // A sideways displacement may fold or move the patch boundary. Don't infer support from its base rectangle.
                if (normals[k] * distances[j] + (offsets?[k] ?? 0) != 0 ||
                    normals[k + 1] * distances[j] + (offsets?[k + 1] ?? 0) != 0)
                    throw new InvalidDataException("The displacement moves sideways; its support footprint cannot be verified.");
                Vector2 xy = corners[corner] + row * i + col * j;
                float z = start[2] + elevation + normals[k + 2] * distances[j] + (offsets?[k + 2] ?? 0);
                if (!float.IsFinite(z)) throw Invalid();
                vertices[i * side + j] = new(xy, z);
            }
        }
        float height = float.NegativeInfinity;
        for (int r = 0; r < cells; r++)
            for (int c = 0; c < cells; c++)
            {
                int a = r * side + c, b = a + 1, d = a + side, e = d + 1;
                if (((r + c) & 1) == 0)
                {
                    height = MathF.Max(height, Triangle(vertices[a], vertices[b], vertices[e], min, max));
                    height = MathF.Max(height, Triangle(vertices[a], vertices[e], vertices[d], min, max));
                }
                else
                {
                    height = MathF.Max(height, Triangle(vertices[a], vertices[b], vertices[d], min, max));
                    height = MathF.Max(height, Triangle(vertices[b], vertices[e], vertices[d], min, max));
                }
            }
        return float.IsFinite(height) ? height : throw Invalid();
    }

    private static float Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 min, Vector2 max)
    {
        Span<Vector3> input = stackalloc Vector3[7], output = stackalloc Vector3[7];
        input[0] = a; input[1] = b; input[2] = c;
        int count = 3;
        for (int edge = 0; edge < 4 && count > 0; edge++)
        {
            int n = 0;
            var prev = input[count - 1];
            float from = Distance(prev, edge, min, max);
            foreach (var next in input[..count])
            {
                float to = Distance(next, edge, min, max);
                if ((from < 0 && to > 0) || (from > 0 && to < 0))
                    output[n++] = Vector3.Lerp(prev, next, from / (from - to));
                if (to >= 0) output[n++] = next;
                prev = next; from = to;
            }
            count = n;
            var swap = input; input = output; output = swap;
        }
        float z = float.NegativeInfinity;
        foreach (var point in input[..count]) z = MathF.Max(z, point.Z);
        return z;
    }

    private static float Distance(Vector3 p, int edge, Vector2 min, Vector2 max) => edge switch
    {
        0 => p.X - min.X, 1 => max.X - p.X, 2 => p.Y - min.Y, _ => max.Y - p.Y,
    };

    private static float[] Numbers(string? text, int count)
    {
        var parts = text?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts?.Length != count) throw Invalid();
        var result = new float[count];
        for (int i = 0; i < count; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]) || !float.IsFinite(result[i]))
                throw Invalid();
        return result;
    }

    private static InvalidDataException Invalid() => new("Unsupported or incomplete displacement geometry.");
}
