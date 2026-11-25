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
        const string dimensions = " 64w 64l 64h @(32 32 32)  ";
        var map = Read(Box(12));
        var selection = BrushSelection.FromStatus("solid with 6 faces   [dist: 100.0]", dimensions);

        Assert.Equal(12, selection!.Resolve(map)?.Id);
        Assert.Null(selection.Resolve(Read(Box(12) + Box(30))));
        Assert.Null(BrushSelection.FromStatus("2 objects selected.", dimensions));
        Assert.Null(BrushSelection.FromStatus("light [dist: 100.0]", dimensions));
    }

    [Fact]
    public void StockHammerIgnoresPlaneSamplesOutsideTheSolidAndDoesNotPickAnInnerBrush()
    {
        var map = Read(Box(12, top: "(100 64 64) (164 64 64) (164 0 64)") + Box(30, 16, 48));
        var selection = BrushSelection.FromStatus("solid with 6 faces", "64w 64l 64h @(32 32 32)")!;

        Assert.Equal(12, selection.Resolve(map)?.Id);
    }

    [Fact]
    public void StockHammerMatchesRoundedBoundsOfASlopedSolid()
    {
        // x, y, z >= 0 and x + y + z <= 63.
        var map = Read(Solid(12,
            "(100 -37 0) (0 -37 100) (0 63 0)",
            "(0 0 0) (64 0 0) (64 64 0)",
            "(0 64 64) (0 0 64) (0 0 0)",
            "(64 0 0) (0 0 0) (0 0 64)"));
        var selection = BrushSelection.FromStatus("solid with 4 faces", "63w 63l 63h @(32 32 32)")!;

        Assert.Equal(12, selection.Resolve(map)?.Id);
    }

    [Theory]
    [InlineData("(0 64 64) (32 64 64) (64 64 64)")] // collinear points
    [InlineData("(64 0 64) (64 64 64) (0 64 64)")] // reversed top; the solid is open
    public void StockHammerDoesNotGuessWhenACandidateHasUnusablePlanes(string top)
    {
        var selection = BrushSelection.FromStatus("solid with 6 faces", "64w 64l 64h @(32 32 32)")!;

        Assert.Null(selection.Resolve(Read(Box(12, top: top))));
        Assert.Null(selection.Resolve(Read(Box(30) + Box(12, top: top))));
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

    private static string Box(int id, int low = 0, int high = 64, string? top = null) => Solid(id,
        top ?? $"({low} {high} {high}) ({high} {high} {high}) ({high} {low} {high})",
        $"({low} {low} {low}) ({high} {low} {low}) ({high} {high} {low})",
        $"({low} {high} {high}) ({low} {low} {high}) ({low} {low} {low})",
        $"({high} {high} {low}) ({high} {low} {low}) ({high} {low} {high})",
        $"({high} {high} {high}) ({low} {high} {high}) ({low} {high} {low})",
        $"({high} {low} {low}) ({low} {low} {low}) ({low} {low} {high})");

    private static string Solid(int id, params string[] planes) =>
        $"solid\n{{\n\"id\" \"{id}\"\n" + string.Concat(planes.Select((plane, i) =>
            $"side\n{{\n\"id\" \"{id * 10 + i}\"\n\"plane\" \"{plane}\"\n\"material\" \"tools/toolsnodraw\"\n}}\n")) + "}\n";

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
