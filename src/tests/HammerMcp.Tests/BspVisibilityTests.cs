using System.Numerics;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class BspVisibilityTests
{
    [Fact]
    public void PointComparisonUsesCoordinatesAndCountsSharedFacesOnce()
    {
        using var before = Fixture(false, [3]);
        using var after = Fixture(true, [2]);
        var point = new Vector3(5, 0, 0);
        var a = BspVisibility.Read(before).At(point);
        var b = BspVisibility.Read(after).At(point);
        Assert.Equal((0, 2, 3), (a!.Cluster, a.VisibleClusters, a.VisibleWorldFaces));
        Assert.Equal((1, 1, 2), (b!.Cluster, b.VisibleClusters, b.VisibleWorldFaces));
        Assert.Equal(-1, b.VisibleWorldFaces - a.VisibleWorldFaces);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ZeroRunMustAdvanceAndStayWithinRow(byte run, bool valid)
    {
        using var file = Fixture(false, [0, run]);
        if (valid)
            Assert.Equal(0, BspVisibility.Read(file).At(Vector3.One)!.VisibleClusters);
        else
            Assert.Throws<InvalidDataException>(() => BspVisibility.Read(file).At(Vector3.One));
    }

    [Fact]
    public void TruncatedRunIsRejected()
    {
        using var file = Fixture(false, [0]);
        Assert.Throws<InvalidDataException>(() => BspVisibility.Read(file).At(Vector3.One));
    }

    private static MemoryStream Fixture(bool swap, byte[] row)
    {
        var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write("VBSP"u8); w.Write(20); w.Write(new byte[1028]);
        void Lump(int index, int version, Action write)
        {
            var start = (int)stream.Position;
            write(); var end = (int)stream.Position;
            stream.Position = 8 + 16 * index;
            w.Write(start); w.Write(end - start); w.Write(version);
            stream.Position = end;
        }
        Lump(1, 0, () => { w.Write(1f); w.Write(new byte[16]); });
        Lump(4, 0, () => { w.Write(2); for (int i = 0; i < 4; i++) w.Write(20); w.Write(row); });
        Lump(5, 0, () => { w.Write(0); w.Write(-1); w.Write(-2); w.Write(new byte[20]); });
        Lump(10, 1, () => {
            for (int i = 0; i < 2; i++) {
                w.Write(0); w.Write((short)(swap ? 1-i : i)); w.Write(new byte[14]);
                w.Write((ushort)(2*i)); w.Write((ushort)2); w.Write(new byte[8]);
            }
        });
        Lump(14, 0, () => { w.Write(new byte[44]); w.Write(3); });
        Lump(16, 0, () => { foreach (ushort face in new ushort[] { 0, 1, 1, 2 }) w.Write(face); });
        stream.Position = 0;
        return stream;
    }
}
