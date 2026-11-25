using System.Buffers.Binary;
using System.Numerics;

namespace HammerMcp.Core.Maps;

public static class PropPlacement
{
    public static Vector3 OnBrush(VmfBrush brush, ReadOnlySpan<byte> mdl, ReadOnlySpan<byte> vvd, float x, float y, Vector3 angles)
    {
        if (brush.Hidden || brush.EntityId is not null || !VmfBrushBounds.IsAxisAlignedBox(brush.Faces) ||
            VmfBrushBounds.FromFaces(brush.Faces) is not { } surface)
            throw new InvalidDataException("Support must be a visible rectangular world brush.");
        if (mdl.Length < 156 || !mdl[..4].SequenceEqual("IDST"u8) || (Int(mdl, 152) & 16) == 0 ||
            vvd.Length < 64 || !vvd[..4].SequenceEqual("IDSV"u8) || Int(vvd, 4) != 4 || Int(vvd, 8) != Int(mdl, 8))
            throw new InvalidDataException("A matching Source 1 static-prop MDL and version 4 VVD are required.");
        int count = Int(vvd, 16), start = Int(vvd, 56);
        if (start < 64 || start > vvd.Length || count <= 0 || count > (vvd.Length - start) / 48)
            throw new InvalidDataException("Invalid VVD vertex range.");
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(angles.X) || !float.IsFinite(angles.Y) || !float.IsFinite(angles.Z))
            throw new InvalidDataException("Coordinates and angles must be finite.");
        const float radians = MathF.PI / 180;
        var rotation = Matrix4x4.CreateRotationX(angles.Z * radians) * Matrix4x4.CreateRotationY(angles.X * radians) * Matrix4x4.CreateRotationZ(angles.Y * radians);
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            int at = start + i * 48 + 16;
            var v = Vector3.Transform(new Vector3(Number(vvd, at), Number(vvd, at + 4), Number(vvd, at + 8)), rotation);
            if (!float.IsFinite(v.Z) || !(x + v.X >= surface.Min.X && x + v.X <= surface.Max.X &&
                                         y + v.Y >= surface.Min.Y && y + v.Y <= surface.Max.Y))
                throw new InvalidDataException("The model footprint does not fit on the support brush.");
            lowest = MathF.Min(lowest, v.Z);
        }
        float z = surface.Max.Z - lowest;
        if (!float.IsFinite(z))
            throw new InvalidDataException("The computed placement height is invalid.");
        return new(x, y, z);
    }

    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static float Number(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
}
