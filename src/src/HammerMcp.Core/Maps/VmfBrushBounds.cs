using System.Globalization;

namespace HammerMcp.Core.Maps;

internal static class VmfBrushBounds
{
    // The starting square must enclose the Source 1 map. If a winding still reaches it,
    // its bounds depend on this arbitrary limit rather than on the brush planes.
    private const double WindingRadius = 131072;
    private const double PlaneEpsilon = 1e-7;

    internal static bool IsAxisAlignedBox(ReadOnlySpan<VmfBrushFace> faces)
    {
        if (faces.Length != 6)
            return false;
        foreach (var face in faces)
        {
            var text = face.Plane.AsSpan();
            if (face.HasDisplacement)
                return false;
            if (!TryReadPoint(ref text, out var a) || !TryReadPoint(ref text, out var b) ||
                !TryReadPoint(ref text, out var c))
                return false;
            if (!(a.X == b.X && a.X == c.X || a.Y == b.Y && a.Y == c.Y || a.Z == b.Z && a.Z == c.Z))
                return false;
        }
        return true;
    }

    public static (BrushPoint Min, BrushPoint Max)? FromFaces(ReadOnlySpan<VmfBrushFace> faces)
    {
        if (faces.Length < 4)
            return null;

        Span<Plane> planes = faces.Length <= 64 ? stackalloc Plane[faces.Length] : new Plane[faces.Length];
        for (int i = 0; i < planes.Length; i++)
        {
            var text = faces[i].Plane.AsSpan();
            if (!TryReadPoint(ref text, out var a) || !TryReadPoint(ref text, out var b) ||
                !TryReadPoint(ref text, out var c) || !text.Trim().IsEmpty)
                return null;

            // Hammer writes points clockwise as viewed from outside the brush.
            var normal = Point.Cross(a - b, c - b);
            var length = Math.Sqrt(Point.Dot(normal, normal));
            if (!(length > 0) || !double.IsFinite(length))
                return null;
            normal *= 1 / length;
            planes[i] = new(normal, Point.Dot(normal, a));
        }

        // A half-plane can add at most one vertex to a convex winding. Reuse both buffers for every face.
        int capacity = planes.Length + 4;
        Span<Point> buffers = faces.Length <= 64 ? stackalloc Point[2 * capacity] : new Point[2 * capacity];
        var winding = buffers[..capacity];
        var clipped = buffers[capacity..];
        var min = new Point(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Point(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (int i = 0; i < planes.Length; i++)
        {
            var plane = planes[i];
            var origin = plane.Normal * plane.Distance;
            var axis = Math.Abs(plane.Normal.Z) < 0.9 ? new Point(0, 0, 1) : new Point(1, 0, 0);
            var u = Point.Cross(axis, plane.Normal);
            u *= WindingRadius / Math.Sqrt(Point.Dot(u, u));
            var v = Point.Cross(plane.Normal, u);
            winding[0] = origin - u - v;
            winding[1] = origin + u - v;
            winding[2] = origin + u + v;
            winding[3] = origin - u + v;
            int count = 4;

            for (int j = 0; j < planes.Length; j++)
            {
                if (j == i)
                    continue;
                count = Clip(winding[..count], clipped, planes[j]);
                if (count < 3)
                    return null;
                var swap = winding;
                winding = clipped;
                clipped = swap;
            }

            double area = 0;
            for (int j = 0; j < count; j++)
            {
                var vertex = winding[j];
                var offset = vertex - origin;
                if (!(Point.Dot(offset, offset) < (WindingRadius - PlaneEpsilon) * (WindingRadius - PlaneEpsilon)))
                    return null;
                min = new(Math.Min(min.X, vertex.X), Math.Min(min.Y, vertex.Y), Math.Min(min.Z, vertex.Z));
                max = new(Math.Max(max.X, vertex.X), Math.Max(max.Y, vertex.Y), Math.Max(max.Z, vertex.Z));
                if (j >= 2)
                    area += Point.Dot(plane.Normal, Point.Cross(winding[j - 1] - winding[0], vertex - winding[0]));
            }
            if (!(area > PlaneEpsilon * PlaneEpsilon))
                return null;
        }

        return min.X < max.X && min.Y < max.Y && min.Z < max.Z
            ? (new((float)min.X, (float)min.Y, (float)min.Z), new((float)max.X, (float)max.Y, (float)max.Z))
            : null;
    }

    private static int Clip(ReadOnlySpan<Point> winding, Span<Point> clipped, Plane plane)
    {
        int count = 0;
        var previous = winding[^1];
        var previousDistance = plane.SignedDistance(previous);
        foreach (var current in winding)
        {
            var distance = plane.SignedDistance(current);
            if ((previousDistance < 0 && distance > 0) || (previousDistance > 0 && distance < 0))
                clipped[count++] = previous + (current - previous) * (previousDistance / (previousDistance - distance));
            if (distance <= 0)
                clipped[count++] = current;
            previous = current;
            previousDistance = distance;
        }
        return count;
    }

    private static bool TryReadPoint(ref ReadOnlySpan<char> text, out Point point)
    {
        point = default;
        text = text.TrimStart();
        int end = text.IndexOf(')');
        if (end < 2 || text[0] != '(')
            return false;
        var values = text[1..end];
        Span<Range> coordinates = stackalloc Range[4];
        if (values.SplitAny(coordinates, " \t", StringSplitOptions.RemoveEmptyEntries) != 3 ||
            !TryNumber(values[coordinates[0]], out var x) || !TryNumber(values[coordinates[1]], out var y) ||
            !TryNumber(values[coordinates[2]], out var z))
            return false;
        point = new(x, y, z);
        text = text[(end + 1)..];
        return true;
    }

    private static bool TryNumber(ReadOnlySpan<char> text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private readonly record struct Plane(Point Normal, double Distance)
    {
        public double SignedDistance(Point point)
        {
            var distance = Point.Dot(Normal, point) - Distance;
            return Math.Abs(distance) <= PlaneEpsilon ? 0 : distance;
        }
    }

    private readonly record struct Point(double X, double Y, double Z)
    {
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point operator *(Point a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);
        public static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Point Cross(Point a, Point b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
