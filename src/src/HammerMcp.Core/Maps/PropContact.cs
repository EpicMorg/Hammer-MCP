using System.Numerics;

namespace HammerMcp.Core.Maps;

public static class PropContact
{
    private const float Epsilon = 0.001f;

    /// <summary>Find a horizontal, upward-facing patch covering the complete rotated footprint envelope.</summary>
    public static Vector3 Place(ReadOnlySpan<Vector3> triangles, ReadOnlySpan<Vector3> prop,
        Vector2 xy, float? surfaceZ)
    {
        if (!float.IsFinite(xy.X) || !float.IsFinite(xy.Y) || surfaceZ is { } hint && !float.IsFinite(hint) ||
            triangles.Length % 3 != 0 || prop.IsEmpty)
            throw new InvalidDataException("Invalid prop placement geometry or coordinates.");
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var v in prop)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        if (!Finite(min) || !Finite(max) || max.X - min.X < 2 * Epsilon || max.Y - min.Y < 2 * Epsilon)
            throw new InvalidDataException("The placed model has an invalid footprint.");

        var candidates = new List<(float Z, Vector2 A, Vector2 B, Vector2 C)>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = Local(triangles[i]); var b = Local(triangles[i + 1]); var c = Local(triangles[i + 2]);
            if (!Finite(a) || !Finite(b) || !Finite(c))
                throw new InvalidDataException("The support model contains invalid geometry.");
            if (!Overlaps(a, b, c, min, max) || MathF.Abs(a.Z - b.Z) > Epsilon ||
                MathF.Abs(a.Z - c.Z) > Epsilon || Vector3.Cross(b - a, c - a).Z <= Epsilon * Epsilon)
                continue;
            candidates.Add((a.Z, new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y)));
        }
        var center = new Vector2((min.X + max.X) / 2, (min.Y + max.Y) / 2);
        float z = float.NaN, best = float.PositiveInfinity;
        foreach (var t in candidates)
        {
            if (Cross(t.B - t.A, center - t.A) < -Epsilon ||
                Cross(t.C - t.B, center - t.B) < -Epsilon ||
                Cross(t.A - t.C, center - t.C) < -Epsilon)
                continue;
            float score = surfaceZ is { } h ? MathF.Abs(t.Z - h) : -t.Z;
            if (score < best || score == best && t.Z > z)
                (best, z) = (score, t.Z);
        }
        if (!float.IsFinite(z) || surfaceZ is not null && best > 8 || !Covered(z))
            throw new InvalidDataException("No horizontal support near the requested height covers the full prop footprint (edge, gap, or unsupported surface).");
        if (Intersects(triangles, z))
            throw new InvalidDataException("The selected support surface has geometry intersecting the placed prop's envelope.");
        float originZ = z - min.Z;
        return float.IsFinite(originZ) ? new(xy, originZ) : throw new InvalidDataException("The computed placement height is invalid.");

        Vector3 Local(Vector3 v) => v - new Vector3(xy, 0);

        bool Covered(float z)
        {
            Vector2[] rectangle = [new(min.X, min.Y), new(max.X, min.Y), new(max.X, max.Y), new(min.X, max.Y)];
            var remaining = new List<Vector2[]> { rectangle };
            var next = new List<Vector2[]>();
            foreach (var t in candidates)
            {
                if (MathF.Abs(t.Z - z) > Epsilon)
                    continue;
                foreach (var poly in remaining)
                {
                    // Subtract the triangle: retain the outside portion of each successive half-plane.
                    var inside = poly;
                    for (int i = 0; i < 3 && inside.Length != 0; i++)
                    {
                        var a = i == 0 ? t.A : i == 1 ? t.B : t.C;
                        var b = i == 0 ? t.B : i == 1 ? t.C : t.A;
                        var outside = Clip(inside, a, b, -1);
                        if (Area(outside) > Epsilon * Epsilon)
                            next.Add(outside);
                        inside = Clip(inside, a, b, 1);
                    }
                }
                if (next.Count == 0)
                    return true;
                if (next.Count > 4096)
                    throw new InvalidDataException("The support surface is too fragmented to verify.");
                (remaining, next) = (next, remaining);
                next.Clear();
            }
            return false;
        }

        bool Intersects(ReadOnlySpan<Vector3> mesh, float z)
        {
            var lo = new Vector3(min.X + Epsilon, min.Y + Epsilon, z + 2 * Epsilon);
            var hi = new Vector3(max.X - Epsilon, max.Y - Epsilon, z + max.Z - min.Z - Epsilon);
            if (hi.Z <= lo.Z)
                return false;
            Span<Vector3> first = stackalloc Vector3[10], second = stackalloc Vector3[10];
            for (int i = 0; i < mesh.Length; i += 3)
            {
                var a = Local(mesh[i]); var b = Local(mesh[i + 1]); var c = Local(mesh[i + 2]);
                if (!Overlaps(a, b, c, lo, hi) || MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) < lo.Z ||
                    MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) > hi.Z)
                    continue;
                first[0] = a; first[1] = b; first[2] = c;
                int count = 3;
                for (int axis = 0; axis < 3 && count != 0; axis++)
                {
                    count = ClipAxis(first[..count], second, axis, lo[axis], 1);
                    count = ClipAxis(second[..count], first, axis, hi[axis], -1);
                }
                if (count != 0)
                    return true;
            }
            return false;
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Overlaps(Vector3 a, Vector3 b, Vector3 c, Vector3 lo, Vector3 hi) =>
        Vector3.Min(a, Vector3.Min(b, c)) is var min && Vector3.Max(a, Vector3.Max(b, c)) is var max &&
        min.X <= hi.X && min.Y <= hi.Y && max.X >= lo.X && max.Y >= lo.Y;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float Area(ReadOnlySpan<Vector2> p)
    {
        float twice = 0;
        for (int i = 1; i + 1 < p.Length; i++)
            twice += Cross(p[i] - p[0], p[i + 1] - p[0]);
        return MathF.Abs(twice) / 2;
    }

    private static Vector2[] Clip(Vector2[] poly, Vector2 a, Vector2 b, int sign)
    {
        var edge = b - a;
        var p = poly[^1];
        float pd = sign * Cross(edge, p - a);
        var result = new List<Vector2>(poly.Length + 1);
        foreach (var q in poly)
        {
            float qd = sign * Cross(edge, q - a);
            if ((pd < 0) != (qd < 0))
                result.Add(Vector2.Lerp(p, q, pd / (pd - qd)));
            if (qd >= 0)
                result.Add(q);
            p = q;
            pd = qd;
        }
        return result.ToArray();
    }

    private static int ClipAxis(ReadOnlySpan<Vector3> poly, Span<Vector3> output, int axis, float limit, int sign)
    {
        if (poly.IsEmpty)
            return 0;
        var p = poly[^1];
        float pd = (p[axis] - limit) * sign;
        int count = 0;
        foreach (var q in poly)
        {
            float qd = (q[axis] - limit) * sign;
            if (pd * qd < 0)
                output[count++] = Vector3.Lerp(p, q, pd / (pd - qd));
            if (qd >= 0)
                output[count++] = q;
            p = q;
            pd = qd;
        }
        return count;
    }
}
