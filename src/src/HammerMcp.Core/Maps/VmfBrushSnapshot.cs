using System.Globalization;
using System.Numerics;

namespace HammerMcp.Core.Maps;

public readonly record struct BrushPoint(float X, float Y, float Z);

public readonly record struct VmfBrushFace(int Id, string Plane, string Material)
{
    public bool HasDisplacement { get; init; }
    internal string? Displacement { get; init; }
}

public sealed record VmfBrush(int Id, int? EntityId, string ClassName, bool Hidden, VmfBrushFace[] Faces,
    BrushPoint PlanePointMin, BrushPoint PlanePointMax)
{
    public bool SameGeometry(VmfBrush other) => Faces.AsSpan().SequenceEqual(other.Faces);
}

/// <summary>Geometry from a VMF written by Hammer's Export command, without reopening the map.</summary>
public sealed record VmfBrushSnapshot(int MapVersion, IReadOnlyList<VmfBrush> Brushes)
{
    public VmfBrush? FindUnique(int id) => Unique(Brushes.Where(brush => brush.Id == id));
    public int EntityCount { get; init; }

    public static VmfBrushSnapshot Parse(TextReader reader)
    {
        var brushes = new List<VmfBrush>();
        var stack = new List<string>();
        var ownerBrushes = new List<Solid>();
        Solid? solid = null;
        var face = default(VmfBrushFace);
        System.Text.StringBuilder? displacement = null;
        string? pending = null;
        int ownerDepth = 0, solidDepth = 0, faceDepth = 0, ownerId = 0, mapVersion = -1;
        var ownerClass = "";
        bool ownerIsEntity = false, hasWorld = false;
        int entityCount = 0;

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (displacement is not null)
            {
                if (stack.Count == faceDepth + 1 && line == "}")
                {
                    face = face with { Displacement = displacement.ToString() };
                    displacement = null;
                }
                else
                    displacement.AppendLine(line);
            }
            if (line[0] == '"')
            {
                if (!VmfEntities.TryKeyValue(line, out var key, out var value))
                    continue;
                int depth = stack.Count;
                if (depth == 1 && stack[0] == "versioninfo" && key == "mapversion")
                    mapVersion = ParseNumber(value);
                else if (ownerDepth != 0 && depth == ownerDepth)
                {
                    if (key == "classname") ownerClass = value;
                    if (key == "id") ownerId = ParseNumber(value);
                }
                else if (solid is not null && depth == solidDepth && key == "id")
                    solid.Id = ParseNumber(value);
                else if (faceDepth != 0 && depth == faceDepth)
                    face = key switch
                    {
                        "id" => face with { Id = ParseNumber(value) },
                        "plane" => face with { Plane = value },
                        "material" => face with { Material = value },
                        _ => face,
                    };
                continue;
            }
            if (line == "{")
            {
                var block = pending ?? throw InvalidSnapshot();
                pending = null;
                stack.Add(block);
                int depth = stack.Count;
                if (ownerDepth == 0 && (depth == 1 || (depth == 2 && stack[0] == "hidden")) && block is "world" or "entity")
                {
                    ownerDepth = depth;
                    ownerId = 0;
                    ownerIsEntity = block == "entity";
                    if (ownerIsEntity) entityCount++;
                    ownerClass = ownerIsEntity ? "?" : "worldspawn";
                    hasWorld |= !ownerIsEntity;
                }
                else if (ownerDepth != 0 && solid is null && block == "solid" &&
                         (depth == ownerDepth + 1 || (depth == ownerDepth + 2 && stack[^2] == "hidden")))
                {
                    solidDepth = depth;
                    solid = new Solid(stack.Contains("hidden"));
                }
                else if (solid is not null && depth == solidDepth + 1 && block == "side")
                {
                    faceDepth = depth;
                    face = default;
                }
                else if (faceDepth != 0 && depth == faceDepth + 1 && block == "dispinfo")
                {
                    face = face with { HasDisplacement = true };
                    displacement = new();
                }
                continue;
            }
            if (line != "}")
            {
                pending = line;
                continue;
            }

            int closingDepth = stack.Count;
            if (closingDepth == 0)
                throw InvalidSnapshot();
            if (closingDepth == faceDepth)
            {
                if (face.Id <= 0 || string.IsNullOrEmpty(face.Plane) || string.IsNullOrEmpty(face.Material))
                    throw InvalidSnapshot();
                solid!.Faces.Add(face);
                faceDepth = 0;
            }
            if (closingDepth == solidDepth)
            {
                ownerBrushes.Add(solid!);
                solid = null;
                solidDepth = 0;
            }
            if (closingDepth == ownerDepth)
            {
                foreach (var brush in ownerBrushes)
                    brushes.Add(brush.Build(ownerIsEntity ? ownerId : null, ownerClass));
                ownerBrushes.Clear();
                ownerDepth = 0;
            }
            stack.RemoveAt(closingDepth - 1);
        }
        if (stack.Count != 0 || !hasWorld || mapVersion < 0)
            throw InvalidSnapshot();
        return new(mapVersion, brushes) { EntityCount = entityCount };
    }

    private static VmfBrush? Unique(IEnumerable<VmfBrush> brushes)
    {
        using var cursor = brushes.GetEnumerator();
        if (!cursor.MoveNext())
            return null;
        var result = cursor.Current;
        return cursor.MoveNext() ? null : result;
    }

    private static int ParseNumber(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        ? id : throw InvalidSnapshot();

    private static InvalidDataException InvalidSnapshot() => new("Hammer did not write a complete, valid VMF snapshot.");

    private sealed class Solid(bool hidden)
    {
        public int Id { get; set; }
        public List<VmfBrushFace> Faces { get; } = [];

        public VmfBrush Build(int? entityId, string className)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            foreach (var face in Faces)
            {
                foreach (var point in VmfEntities.PlanePoints(face.Plane))
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }
            if (Id <= 0 || !float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(min.Z) ||
                !float.IsFinite(max.X) || !float.IsFinite(max.Y) || !float.IsFinite(max.Z))
                throw InvalidSnapshot();
            Faces.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            return new(Id, entityId, className, hidden, Faces.ToArray(), new(min.X, min.Y, min.Z), new(max.X, max.Y, max.Z));
        }
    }
}
