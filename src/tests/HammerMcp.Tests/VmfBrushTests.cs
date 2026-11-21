using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class VmfBrushTests
{
    [Fact]
    public void HammerPlusPlusUsesTheSelectedBrushIdEvenWhenBoundsOverlap()
    {
        var map = Read(Brush(12, 14) + Brush(30, 31));
        var selection = BrushSelection.FromStatus("solid with 1 faces  [ID: 30] [dist: 100.0]", "");

        Assert.Equal(30, selection!.Resolve(map)?.Id);
        Assert.Null(BrushSelection.FromStatus("solid with 1 faces  [ID: 29]", "")!.Resolve(map));
    }

    [Fact]
    public void StockHammerResolvesOnlyAnUnambiguousSingleSolid()
    {
        const string dimensions = " 64w 64l 0h @(32 32 64)  ";
        var map = Read(Brush(12, 14));
        var selection = BrushSelection.FromStatus("solid with 1 faces   [dist: 100.0]", dimensions);

        Assert.Equal(12, selection!.Resolve(map)?.Id);
        Assert.Null(selection.Resolve(Read(Brush(12, 14) + Brush(30, 31))));
        Assert.Null(BrushSelection.FromStatus("2 objects selected.", dimensions));
        Assert.Null(BrushSelection.FromStatus("light [dist: 100.0]", dimensions));
    }

    [Fact]
    public void HiddenBrushKeepsItsEntityAndMaterial()
    {
        var map = Read("", "hidden\n{\nentity\n{\n\"id\" \"22\"\n\"classname\" \"func_detail\"\n" + Brush(45, 46) + "}\n}\n");
        var brush = Assert.Single(map.Brushes);

        Assert.Equal(45, brush.Id);
        Assert.Equal(22, brush.EntityId);
        Assert.Equal("func_detail", brush.ClassName);
        Assert.True(brush.Hidden);
        Assert.Equal("tools/toolsnodraw", Assert.Single(brush.Faces).Material);
        Assert.Equal(new BrushPoint(0, 0, 64), brush.PlanePointMin);
        Assert.Equal(new BrushPoint(64, 64, 64), brush.PlanePointMax);
    }

    private static VmfBrushSnapshot Read(string world, string entities = "") => VmfBrushSnapshot.Parse(new StringReader(
        "versioninfo\n{\n\"mapversion\" \"7\"\n}\nworld\n{\n\"id\" \"1\"\n\"classname\" \"worldspawn\"\n" + world + "}\n" + entities));

    private static string Brush(int id, int faceId) => "solid\n{\n" + $$"""
        "id" "{{id}}"
        side
        {
            "id" "{{faceId}}"
            "plane" "(0 64 64) (64 64 64) (64 0 64)"
            "material" "tools/toolsnodraw"
        }
        }
        """ + "\n";
}
