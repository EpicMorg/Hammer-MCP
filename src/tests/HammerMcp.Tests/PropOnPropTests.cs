using System.Numerics;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class PropOnPropTests
{
    private static readonly Vector3[] Box = [new(-1, -1, -2), new(1, 1, 3)];

    private static Vector3[] Quad(float x, float end, float z, float y = 4) =>
        [new(x, -y, z), new(end, -y, z), new(end, y, z), new(x, -y, z), new(end, y, z), new(x, y, z)];

    [Fact]
    public void SeatsBoxOnRotatedScaledDeckButRefusesGapAndEdge()
    {
        var deck = Quad(-4, -1, 2).Concat(Quad(1, 4, 2)).ToArray();
        var transform = Matrix4x4.CreateScale(2) * Matrix4x4.CreateRotationZ(MathF.PI / 2) *
                        Matrix4x4.CreateTranslation(100, 200, 300);
        for (int i = 0; i < deck.Length; i++)
            deck[i] = Vector3.Transform(deck[i], transform);
        Assert.Equal(new Vector3(100, 205, 306), PropContact.Place(deck, Box, new(100, 205), null));
        Assert.Throws<InvalidDataException>(() => PropContact.Place(deck, Box, new(100, 200), null));
        Assert.Throws<InvalidDataException>(() => PropContact.Place(deck, Box, new(100, 208), null));
    }

    [Fact]
    public void SelectsTheRequestedShelfAndRefusesAnOverhangInsideTheProp()
    {
        var shelves = Quad(-4, 4, 4).Concat(Quad(-4, 4, 16)).ToArray();
        Assert.Equal(new Vector3(0, 0, 6), PropContact.Place(shelves, Box, Vector2.Zero, 5));
        Assert.Equal(new Vector3(0, 0, 18), PropContact.Place(shelves, Box, Vector2.Zero, null));
        var obstructed = shelves.Concat(new[] { new Vector3(0, -2, 3), new Vector3(0, 2, 3), new Vector3(0, 0, 9) }).ToArray();
        Assert.Throws<InvalidDataException>(() => PropContact.Place(obstructed, Box, Vector2.Zero, 5));
    }

    [Fact]
    public void DoesNotDropThroughGapToAnUnrequestedShelf()
    {
        var shelves = Quad(-4, 4, 0).Concat(Quad(-4, -2, 24)).Concat(Quad(2, 4, 24)).ToArray();
        Assert.Throws<InvalidDataException>(() => PropContact.Place(shelves, Box, Vector2.Zero, 24));
        Assert.Throws<InvalidDataException>(() => PropContact.Place(shelves, Box, new(2.5f, 0), null));
    }
}
