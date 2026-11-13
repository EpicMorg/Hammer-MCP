using System.Numerics;
using Datamodel;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class VmapMapTests
{
    private static Element Node(Datamodel.Datamodel dm, string cls, Vector3 origin, float yaw = 0)
    {
        var e = new Element(dm, "", null, cls);
        e["origin"] = origin;
        e["angles"] = new QAngle(0, yaw, 0);
        e["scales"] = Vector3.One;
        e["children"] = new ElementArray();
        return e;
    }

    [Fact]
    public void ReadsEntitiesInWorldSpaceWithOutputs()
    {
        var dm = new Datamodel.Datamodel("vmap", 23);
        var root = new Element(dm, "", null, "CMapRootElement");
        dm.Root = root;
        root["editorbuild"] = 9999;
        root["editorversion"] = 400;
        root["isprefab"] = false;
        var world = Node(dm, "CMapWorld", Vector3.Zero);
        var worldProps = new Element(dm, "", null, "EditGameClassProps");
        worldProps["classname"] = "worldspawn";
        worldProps["skyname"] = "sky_test";
        world["entity_properties"] = worldProps;
        root["world"] = world;

        var group = Node(dm, "CMapGroup", new Vector3(100, 0, 0), yaw: 90);
        ((ElementArray)world["children"]!).Add(group);
        var relay = Node(dm, "CMapEntity", new Vector3(10, 0, 0));
        relay["nodeID"] = 7;
        var props = new Element(dm, "", null, "EditGameClassProps");
        props["classname"] = "logic_relay";
        props["targetname"] = "relay";
        relay["entity_properties"] = props;
        var conn = new Element(dm, "", null, "DmeConnectionData");
        conn["outputName"] = "OnTrigger";
        conn["targetName"] = "door";
        conn["inputName"] = "Open";
        conn["overrideParam"] = "";
        conn["delay"] = 0.5f;
        conn["timesToFire"] = -1;
        relay["connectionsData"] = new ElementArray { conn };
        ((ElementArray)group["children"]!).Add(relay);

        var path = Path.Combine(Path.GetTempPath(), $"hammermcp-{Guid.NewGuid():N}.vmap");
        dm.Save(path, "binary", 9);

        var r = VmapMap.Read(path);
        Assert.Equal(9999, r.Summary.EditorBuild);
        Assert.Equal("sky_test", r.Summary.SkyName);
        var e = Assert.Single(r.Entities);
        Assert.Equal(7, e.Id);
        Assert.Equal("logic_relay", e.ClassName);
        Assert.Equal("relay", e.TargetName);
        Assert.True(VmfEntities.SameOrigin(new Vector3(100, 10, 0), e.Origin!.Value), e.KeyValues["origin"]);
        var c = Assert.Single(e.Connections);
        Assert.Equal(("OnTrigger", "door", "Open", 0.5f), (c.Output, c.Target, c.Input, c.Delay));
        Assert.Equal(7, Assert.Single(MapEntities.Read(path)).Id);
    }
}
