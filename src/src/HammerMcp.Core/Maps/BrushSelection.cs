using System.Globalization;
using System.Text.RegularExpressions;

namespace HammerMcp.Core.Maps;

/// <summary>A single solid as reported by the editor's accessible status bar.</summary>
public sealed record BrushSelection(int Faces, int? Id, BrushPoint Center, BrushPoint Size)
{
    private static readonly Regex Solid = new(@"^solid with ([0-9]+) faces?(?:\s+\[ID:\s*([0-9]+)\])?(?:\s+\[dist:[^\]]+\])?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Bounds = new(@"^\s*(\S+)w\s+(\S+)l\s+(\S+)h\s+@\(\s*(\S+)\s+(\S+)\s+([^\s)]+)\s*\)\s*$");

    public static BrushSelection? FromStatus(string kind, string dimensions)
    {
        var solid = Solid.Match(kind);
        if (!solid.Success || !int.TryParse(solid.Groups[1].ValueSpan, out int faces) || faces < 1)
            return null;
        if (int.TryParse(solid.Groups[2].ValueSpan, out int id) && id > 0)
            return new(faces, id, default, default);

        // Stock Hammer shows the bounding box and face count, but not the ID.
        var bounds = Bounds.Match(dimensions);
        if (!bounds.Success || !Point(bounds.Groups, 1, out var size) || !Point(bounds.Groups, 4, out var center) ||
            size.X < 0 || size.Y < 0 || size.Z < 0)
            return null;
        return new(faces, null, center, size);
    }

    public VmfBrush? Resolve(VmfBrushSnapshot snapshot)
    {
        if (Id is { } id)
            return snapshot.FindUnique(id) is { } exact && Matches(exact) ? exact : null;

        VmfBrush? result = null;
        foreach (var brush in snapshot.Brushes)
        {
            if (!IsCandidate(brush))
                continue;
            // Unusable geometry could belong to the selection; don't infer a different ID.
            if (VmfBrushBounds.FromFaces(brush.Faces) is not { } bounds)
                return null;
            if (!MatchesBounds(bounds.Min, bounds.Max))
                continue;
            if (result is not null)
                return null;
            result = brush;
        }
        return result is null ? null : snapshot.FindUnique(result.Id);
    }

    public bool Matches(VmfBrush brush) => IsCandidate(brush) &&
        (Id is { } id ? id == brush.Id :
            VmfBrushBounds.FromFaces(brush.Faces) is { } bounds && MatchesBounds(bounds.Min, bounds.Max));

    private bool IsCandidate(VmfBrush brush) => !brush.Hidden && brush.Faces.Length == Faces;

    private bool MatchesBounds(BrushPoint min, BrushPoint max) =>
        MatchesAxis(min.X, max.X, Center.X, Size.X) &&
        MatchesAxis(min.Y, max.Y, Center.Y, Size.Y) &&
        MatchesAxis(min.Z, max.Z, Center.Z, Size.Z);

    // Hammer rounds the status bar's center and size independently.
    private static bool MatchesAxis(float min, float max, float center, float size) =>
        MathF.Abs((min + max) * 0.5f - center) <= 0.51f && MathF.Abs(max - min - size) <= 0.51f;

    private static bool Point(GroupCollection groups, int first, out BrushPoint point)
    {
        bool ok = Number(groups[first], out var x) & Number(groups[first + 1], out var y) & Number(groups[first + 2], out var z);
        point = new(x, y, z);
        return ok;
    }

    private static bool Number(Group group, out float n) =>
        float.TryParse(group.ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && float.IsFinite(n);
}
