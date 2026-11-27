using System.Buffers.Binary;
using System.Numerics;

namespace HammerMcp.Core.Maps;

/// <summary>Offline visibility of world-model faces in uncompressed Source 1 VBSP files.</summary>
public sealed class BspVisibility
{
    public readonly record struct Point(float X, float Y, float Z);
    public sealed record Sample(int Cluster, int VisibleClusters, int VisibleWorldFaces, Point LeafMin, Point LeafMax);
    private readonly record struct Leaf(int Cluster, Point Min, Point Max);
    private readonly record struct Node(int Plane, int Front, int Back);
    private readonly record struct Plane(Vector3 Normal, float Distance);
    private readonly record struct Lump(int Offset, int Length, int Version, int UncompressedSize);

    private readonly Plane[] _planes;
    private readonly Node[] _nodes;
    private readonly Leaf[] _leaves;
    private readonly int _root;
    private readonly int[][] _facesByCluster;
    private readonly int[] _representatives;
    private readonly byte[] _vis;
    private readonly int[] _rowStarts;

    public int Version { get; }
    public int Clusters => _facesByCluster.Length;
    public int WorldFaces { get; }

    private BspVisibility(int version, Plane[] planes, Node[] nodes, Leaf[] leaves, int root, int[][] facesByCluster,
        int[] representatives, byte[] vis, int[] rowStarts, int worldFaces)
    {
        Version = version;
        _planes = planes;
        _nodes = nodes;
        _leaves = leaves;
        _root = root;
        _facesByCluster = facesByCluster;
        _representatives = representatives;
        _vis = vis;
        _rowStarts = rowStarts;
        WorldFaces = worldFaces;
    }

    public static BspVisibility Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static BspVisibility Read(Stream stream)
    {
        const int headerSize = 1036;
        Span<byte> header = stackalloc byte[headerSize];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("VBSP"u8))
            throw new InvalidDataException("Not a Source 1 VBSP file.");
        var version = I32(header, 4);
        if (version is < 19 or > 21)
            throw new NotSupportedException($"Unsupported VBSP version {version}; expected standard Source 1 version 19–21.");
        var lumps = new Lump[64];
        for (int i = 0; i < lumps.Length; i++)
        {
            int offset = 8 + i * 16;
            lumps[i] = new(I32(header, offset), I32(header, offset + 4), I32(header, offset + 8), I32(header, offset + 12));
        }
        byte[] Load(int index, int stride, int? expectedVersion = 0)
        {
            var lump = lumps[index];
            if (expectedVersion is { } expected && lump.Version != expected)
                throw new NotSupportedException($"Unsupported BSP lump {index} version {lump.Version}.");
            if (lump.UncompressedSize != 0)
                throw new NotSupportedException($"BSP lump {index} is compressed. Use an uncompressed BSP.");
            if (lump.Length < 0 || lump.Length > 64 * 1024 * 1024 || lump.Length % stride != 0 ||
                (lump.Length != 0 && (lump.Offset < headerSize || (long)lump.Offset + lump.Length > stream.Length)))
                throw new InvalidDataException($"Invalid BSP lump {index} bounds or layout.");
            if (lump.Length == 0)
                return [];
            var data = GC.AllocateUninitializedArray<byte>(lump.Length);
            stream.Position = lump.Offset;
            stream.ReadExactly(data);
            if (data.AsSpan().StartsWith("LZMA"u8))
                throw new NotSupportedException($"BSP lump {index} is compressed. Use an uncompressed BSP.");
            return data;
        }

        var vis = Load(4, 1);
        if (vis.Length == 0)
            throw new InvalidDataException("BSP has no VIS data. Use a BSP compiled with VVIS.");
        if (vis.Length < 4)
            throw new InvalidDataException("Truncated BSP VIS header.");
        int clusters = I32(vis, 0);
        if (clusters is < 1 or > 32768 || 4 + 8 * clusters >= vis.Length)
            throw new InvalidDataException("Invalid BSP VIS cluster table.");
        var offsets = new HashSet<int> { vis.Length };
        for (int i = 0; i < clusters * 2; i++)
        {
            int offset = I32(vis, 4 + i * 4);
            if (offset == -1)
                continue;
            if (offset < 4 + 8 * clusters || offset >= vis.Length)
                throw new InvalidDataException("Invalid BSP visibility row offset.");
            offsets.Add(offset);
        }
        int[] rowStarts = [.. offsets];
        Array.Sort(rowStarts);
        var models = Load(14, 48);
        if (models.Length == 0)
            throw new InvalidDataException("BSP has no world model.");
        int root = I32(models, 36), firstFace = I32(models, 40), faceCount = I32(models, 44);
        if (firstFace < 0 || faceCount < 0 || firstFace + (long)faceCount > 65536)
            throw new InvalidDataException("Invalid BSP world face range.");

        var planeData = Load(1, 20);
        var planes = new Plane[planeData.Length / 20];
        for (int i = 0; i < planes.Length; i++)
        {
            int p = i * 20;
            var normal = new Vector3(F32(planeData, p), F32(planeData, p + 4), F32(planeData, p + 8));
            var distance = F32(planeData, p + 12);
            if (!float.IsFinite(normal.LengthSquared()) || normal == Vector3.Zero || !float.IsFinite(distance))
                throw new InvalidDataException("Invalid BSP plane.");
            planes[i] = new(normal, distance);
        }
        var nodeData = Load(5, 32);
        var nodes = new Node[nodeData.Length / 32];
        var leafStride = lumps[10].Version switch
        {
            0 => 56,
            1 => 32,
            _ => throw new NotSupportedException($"Unsupported BSP leaf version {lumps[10].Version}."),
        };
        var leafData = Load(10, leafStride, null);
        var leaves = new Leaf[leafData.Length / leafStride];
        ValidateChild(root, nodes.Length, leaves.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            int p = i * 32;
            var node = new Node(I32(nodeData, p), I32(nodeData, p + 4), I32(nodeData, p + 8));
            if ((uint)node.Plane >= (uint)planes.Length)
                throw new InvalidDataException("Invalid BSP node plane reference.");
            ValidateChild(node.Front, nodes.Length, leaves.Length);
            ValidateChild(node.Back, nodes.Length, leaves.Length);
            nodes[i] = node;
        }

        var leafFaces = Load(16, 2);
        var grouped = new HashSet<int>?[clusters];
        var representatives = new int[clusters];
        Array.Fill(representatives, -1);
        for (int i = 0; i < leaves.Length; i++)
        {
            int p = i * leafStride;
            int cluster = I16(leafData, p + 4);
            int first = U16(leafData, p + 20), count = U16(leafData, p + 22);
            if (cluster < -1 || cluster >= clusters || first + count > leafFaces.Length / 2)
                throw new InvalidDataException("Invalid BSP leaf cluster or face reference.");
            bool solid = (I32(leafData, p) & 1) != 0;
            leaves[i] = new(solid ? -1 : cluster, ReadPoint(leafData, p + 8), ReadPoint(leafData, p + 14));
            if (solid || cluster < 0)
                continue;
            if (representatives[cluster] == -1)
                representatives[cluster] = i;
            var set = grouped[cluster] ??= [];
            for (int j = first; j < first + count; j++)
            {
                int face = U16(leafFaces, j * 2) - firstFace;
                if ((uint)face < (uint)faceCount)
                    set.Add(face);
            }
        }
        var faces = new int[clusters][];
        for (int i = 0; i < faces.Length; i++)
            faces[i] = grouped[i] is { Count: > 0 } set ? [.. set] : [];
        return new(version, planes, nodes, leaves, root, faces, representatives, vis, rowStarts, faceCount);
    }

    public Sample? At(Vector3 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
            throw new ArgumentException("Coordinates must be finite.", nameof(point));
        int index = _root;
        for (int steps = 0; index >= 0; steps++)
        {
            if (steps == _nodes.Length)
                throw new InvalidDataException("Cycle in the BSP node tree.");
            var node = _nodes[index];
            var plane = _planes[node.Plane];
            index = Vector3.Dot(plane.Normal, point) >= plane.Distance ? node.Front : node.Back;
        }
        var leaf = _leaves[~index];
        return leaf.Cluster == -1 ? null : Measure(leaf, new int[WorldFaces], 1);
    }

    public IReadOnlyList<Sample> Rank(int limit, CancellationToken cancellationToken = default)
    {
        var marks = new int[WorldFaces];
        var top = new PriorityQueue<Sample, (int Faces, int Tie)>(limit);
        for (int cluster = 0; cluster < Clusters; cluster++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_representatives[cluster] is not -1)
            {
                var sample = Measure(_leaves[_representatives[cluster]], marks, cluster + 1);
                top.Enqueue(sample, (sample.VisibleWorldFaces, -cluster));
                if (top.Count > limit)
                    top.Dequeue();
            }
        }
        var result = new Sample[top.Count];
        for (int i = result.Length - 1; i >= 0; i--)
            result[i] = top.Dequeue();
        return result;
    }

    private Sample Measure(Leaf leaf, int[] marks, int generation)
    {
        int cursor = I32(_vis, 4 + 8 * leaf.Cluster);
        if (cursor < 4 + 8 * Clusters || cursor >= _vis.Length)
            throw new InvalidDataException($"Missing or invalid PVS for cluster {leaf.Cluster}.");
        int end = _rowStarts[Array.BinarySearch(_rowStarts, cursor) + 1];
        int faces = 0, clusters = 0;
        for (int start = 0; start < Clusters;)
        {
            if (cursor >= end)
                throw new InvalidDataException("Truncated BSP PVS row.");
            int bits = _vis[cursor++];
            if (bits == 0)
            {
                if (cursor >= end || _vis[cursor] == 0 || _vis[cursor] > (Clusters - start + 7) / 8)
                    throw new InvalidDataException("Invalid BSP PVS zero run.");
                start += 8 * _vis[cursor++];
                continue;
            }
            while (bits != 0)
            {
                int cluster = start + BitOperations.TrailingZeroCount((uint)bits);
                bits &= bits - 1;
                if (cluster >= Clusters)
                    break;
                clusters++;
                foreach (int face in _facesByCluster[cluster])
                {
                    if (marks[face] == generation)
                        continue;
                    marks[face] = generation;
                    faces++;
                }
            }
            start += 8;
        }
        return new(leaf.Cluster, clusters, faces, leaf.Min, leaf.Max);
    }

    private static void ValidateChild(int index, int nodes, int leaves)
    {
        if (index >= 0 ? index >= nodes : ~index >= leaves)
            throw new InvalidDataException("Invalid BSP tree reference.");
    }

    private static int I32(ReadOnlySpan<byte> data, int p) => BinaryPrimitives.ReadInt32LittleEndian(data[p..]);
    private static float F32(ReadOnlySpan<byte> data, int p) => BinaryPrimitives.ReadSingleLittleEndian(data[p..]);
    private static short I16(ReadOnlySpan<byte> data, int p) => BinaryPrimitives.ReadInt16LittleEndian(data[p..]);
    private static ushort U16(ReadOnlySpan<byte> data, int p) => BinaryPrimitives.ReadUInt16LittleEndian(data[p..]);
    private static Point ReadPoint(byte[] data, int p) => new(I16(data, p), I16(data, p + 2), I16(data, p + 4));
}
