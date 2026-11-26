using System.Buffers.Binary;
using System.Numerics;

namespace HammerMcp.Core.Maps;

/// <summary>Source SDK 2013 studio v44–49, VVD v4 and packed DX90 VTX v7, at LOD 0.</summary>
public static class StudioMesh
{
    private const int VertexSize = 48;

    public static Vector3[] Read(ReadOnlySpan<byte> mdl, ReadOnlySpan<byte> vvd, ReadOnlySpan<byte> vtx,
        Matrix4x4 transform, int body = 0, bool requireStatic = false)
    {
        var model = new Table(mdl);
        var vertices = new Table(vvd);
        var topology = new Table(vtx);
        if (mdl.Length < 240 || !mdl[..4].SequenceEqual("IDST"u8) || model.Int(4) is < 44 or > 49 ||
            vvd.Length < 64 || !vvd[..4].SequenceEqual("IDSV"u8) || vertices.Int(4) != 4 ||
            topology.Int(0) != 7 || topology.Int(16) != model.Int(8) || vertices.Int(8) != model.Int(8) ||
            body < 0 || (requireStatic && (model.Int(152) & 16) == 0))
            throw new InvalidDataException("Expected matching Source 1 MDL, VVD and DX90 VTX files for a supported prop.");
        int vertexCount = vertices.Int(16);
        int vertexStart = vertices.Array(0, vertices.Int(56), vertexCount, VertexSize);
        int fixCount = vertices.Int(48);
        int[]? fixups = null;
        if (fixCount != 0)
        {
            int fix = vertices.Array(0, vertices.Int(52), fixCount, 12);
            fixups = new int[vertexCount];
            int filled = 0;
            for (int i = 0; i < fixCount; i++, fix += 12)
            {
                int source = vertices.Int(fix + 4), count = vertices.Int(fix + 8);
                if (count < 0 || source < 0 || count > vertexCount - source || count > vertexCount - filled)
                    throw Invalid();
                for (int k = 0; k < count; k++)
                    fixups[filled++] = source + k;
            }
            if (filled != vertexCount)
                throw Invalid();
        }

        int parts = model.Int(232);
        if (parts <= 0 || parts != topology.Int(28))
            throw Invalid();
        int bp = model.Array(0, model.Int(236), parts, 16);
        int tp = topology.Array(0, topology.Int(32), parts, 8);
        var result = new List<Vector3>();
        for (int i = 0; i < parts; i++, bp += 16, tp += 8)
        {
            int variants = model.Int(bp + 4), basis = model.Int(bp + 8);
            if (variants <= 0 || variants != topology.Int(tp) || basis <= 0)
                throw Invalid();
            int variant = body / basis % variants;
            int sm = model.Array(bp, model.Int(bp + 12), variants, 148) + variant * 148;
            int tm = topology.Array(tp, topology.Int(tp + 4), variants, 8) + variant * 8;
            int lod = topology.Array(tm, topology.Int(tm + 4), topology.Int(tm), 12);
            int meshes = model.Int(sm + 72), modelVertex = model.Int(sm + 84);
            if (topology.Int(tm) <= 0 || meshes != topology.Int(lod) || modelVertex < 0 || modelVertex % VertexSize != 0)
                throw Invalid();
            int mm = model.Array(sm, model.Int(sm + 76), meshes, 116);
            int vm = topology.Array(lod, topology.Int(lod + 4), meshes, 9);
            for (int m = 0; m < meshes; m++, mm += 116, vm += 9)
            {
                if (model.Int(mm + 16) != 0)
                    throw new InvalidDataException("Flexed prop geometry cannot be verified.");
                int meshCount = model.Int(mm + 8), meshOffset = model.Int(mm + 12);
                if (meshOffset < 0 || meshCount < 0 || (long)modelVertex / VertexSize + meshOffset + meshCount > vertexCount)
                    throw Invalid();
                int groups = topology.Int(vm);
                int group = topology.Array(vm, topology.Int(vm + 4), groups, 25);
                for (int g = 0; g < groups; g++, group += 25)
                {
                    int vertexRecords = topology.Int(group), indexCount = topology.Int(group + 8), stripCount = topology.Int(group + 16);
                    int vg = topology.Array(group, topology.Int(group + 4), vertexRecords, 9);
                    int ig = topology.Array(group, topology.Int(group + 12), indexCount, 2);
                    int sg = topology.Array(group, topology.Int(group + 20), stripCount, 27);
                    for (int s = 0; s < stripCount; s++, sg += 27)
                    {
                        int count = topology.Int(sg), first = topology.Int(sg + 4);
                        int flags = topology.Byte(sg + 18);
                        bool list = flags == 1;
                        if (first < 0 || count < 0 || count > indexCount - first ||
                            !(list ? count % 3 == 0 : flags == 2))
                            throw Invalid();
                        for (int j = 0; j + 2 < count; j += list ? 3 : 1)
                        {
                            int a = Vertex(topology, ig, first + j, vg, vertexRecords, meshCount);
                            int b = Vertex(topology, ig, first + j + 1, vg, vertexRecords, meshCount);
                            int c = Vertex(topology, ig, first + j + 2, vg, vertexRecords, meshCount);
                            if (a == b || b == c || a == c)
                                continue;
                            int start = modelVertex / VertexSize + meshOffset;
                            int va = Address(start + a, vertexStart, fixups), vb = Address(start + b, vertexStart, fixups),
                                vc = Address(start + c, vertexStart, fixups);
                            var p = vertices.Vector(va + 16); var q = vertices.Vector(vb + 16); var r = vertices.Vector(vc + 16);
                            var normal = vertices.Vector(va + 28) + vertices.Vector(vb + 28) + vertices.Vector(vc + 28);
                            // Render indices can be clockwise; vertex normals identify the outward side.
                            if (Vector3.Dot(Vector3.Cross(q - p, r - p), normal) < 0)
                                (q, r) = (r, q);
                            if (result.Count >= 3_000_000)
                                throw new InvalidDataException("The prop mesh exceeds the placement geometry limit.");
                            result.Add(Vector3.Transform(p, transform));
                            result.Add(Vector3.Transform(q, transform));
                            result.Add(Vector3.Transform(r, transform));
                        }
                    }
                }
            }
        }
        if (result.Count == 0)
            throw Invalid();
        return result.ToArray();
    }

    public static Matrix4x4 Transform(Vector3 origin, Vector3 angles, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || !Finite(angles) || !Finite(origin))
            throw new InvalidDataException("Invalid prop position, angles or scale.");
        const float radians = MathF.PI / 180;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(angles.Z * radians) *
               Matrix4x4.CreateRotationY(angles.X * radians) * Matrix4x4.CreateRotationZ(angles.Y * radians) *
               Matrix4x4.CreateTranslation(origin);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static int Vertex(Table vtx, int indices, int index, int group, int count, int meshCount)
    {
        int vertex = vtx.UShort(indices + 2 * index);
        if (vertex >= count)
            throw Invalid();
        int original = vtx.UShort(group + 9 * vertex + 4);
        return original < meshCount ? original : throw Invalid();
    }

    private static int Address(int index, int start, int[]? fixups) => start + VertexSize * (fixups?[index] ?? index);

    private static InvalidDataException Invalid() => new("Malformed or unsupported Source 1 studio mesh.");

    private readonly ref struct Table(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public byte Byte(int offset) => Slice(offset, 1)[0];
        public int UShort(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(offset, 2));
        public int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Slice(offset, 4));
        public Vector3 Vector(int offset) => new(BitConverter.Int32BitsToSingle(Int(offset)),
            BitConverter.Int32BitsToSingle(Int(offset + 4)), BitConverter.Int32BitsToSingle(Int(offset + 8)));

        public int Array(int start, int relative, int count, int stride)
        {
            long at = (long)start + relative;
            if (relative < 0 || count < 0 || at + (long)count * stride > _data.Length)
                throw Invalid();
            return (int)at;
        }

        private ReadOnlySpan<byte> Slice(int offset, int length) => offset >= 0 && offset <= _data.Length - length
            ? _data.Slice(offset, length) : throw Invalid();
    }
}
