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
        var e = Assert.Single(r.Entities, x => x.ClassName != "worldspawn");
        Assert.Equal(7, e.Id);
        Assert.Equal("logic_relay", e.ClassName);
        Assert.Equal("relay", e.TargetName);
        Assert.True(VmfEntities.SameOrigin(new Vector3(100, 10, 0), e.Origin!.Value), e.KeyValues["origin"]);
        var c = Assert.Single(e.Connections);
        Assert.Equal(("OnTrigger", "door", "Open", 0.5f), (c.Output, c.Target, c.Input, c.Delay));
        Assert.Equal([7, 0], MapEntities.Read(path).Select(x => x.Id));
    }

    [Fact]
    public void ExpandsPrefabsWithTheirTransform()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hammermcp-pf-{Guid.NewGuid():N}", "content", "mod", "maps");
        Directory.CreateDirectory(Path.Combine(dir, "prefabs"));

        // prefab: one info_target at 10 0 0 (nodeID 2, same as an entity of the main map)
        var pdm = new Datamodel.Datamodel("vmap", 23);
        var proot = new Element(pdm, "", null, "CMapRootElement");
        pdm.Root = proot;
        var pworld = Node(pdm, "CMapWorld", Vector3.Zero);
        proot["world"] = pworld;
        var target = Node(pdm, "CMapEntity", new Vector3(10, 0, 0));
        target["nodeID"] = 2;
        var tprops = new Element(pdm, "", null, "EditGameClassProps");
        tprops["classname"] = "info_target";
        tprops["targetname"] = "inside";
        target["entity_properties"] = tprops;
        // targetname bound to the variable "Name", set through the choice group "Where"
        target["variableTargetKeys"] = new StringArray { "targetname" };
        target["variableNames"] = new StringArray { "Name" };
        ((ElementArray)pworld["children"]!).Add(target);
        var vars = new Element(pdm, "", null, "CMapVariableSet");
        vars["variableNames"] = new StringArray { "Name" };
        vars["variableValues"] = new StringArray { "inside" };
        var group = new Element(pdm, "Where", null, "CMapVariableChoiceGroup");
        group["m_ChoiceVariables"] = new StringArray { "Name" };
        var choice = new Element(pdm, "left", null, "CMapVariableChoice");
        choice["m_ChoiceValues"] = new StringArray { "inside_left" };
        group["m_Choices"] = new ElementArray { choice };
        group["m_ActiveValue"] = "";
        vars["m_ChoiceGroups"] = new ElementArray { group };
        proot["mapVariables"] = vars;
        pdm.Save(Path.Combine(dir, "prefabs", "box.vmap"), "binary", 9);

        // map: an entity with nodeID 2 and the prefab placed at 100 0 0 rotated 90 degrees
        var dm = new Datamodel.Datamodel("vmap", 23);
        var root = new Element(dm, "", null, "CMapRootElement");
        dm.Root = root;
        var world = Node(dm, "CMapWorld", Vector3.Zero);
        root["world"] = world;
        var own = Node(dm, "CMapEntity", Vector3.Zero);
        own["nodeID"] = 2;
        var oprops = new Element(dm, "", null, "EditGameClassProps");
        oprops["classname"] = "logic_relay";
        own["entity_properties"] = oprops;
        var prefab = Node(dm, "CMapPrefab", new Vector3(100, 0, 0), yaw: 90);
        prefab["nodeID"] = 5;
        prefab["targetMapPath"] = "maps/prefabs/box.vmap";
        prefab["targetName"] = "box1";
        prefab["variableOverrideNames"] = new StringArray { "Where" };
        prefab["variableOverrideValues"] = new StringArray { "left" };
        ((ElementArray)world["children"]!).Add(own);
        ((ElementArray)world["children"]!).Add(prefab);
        var path = Path.Combine(dir, "main.vmap");
        dm.Save(path, "binary", 9);

        var r = VmapMap.Read(path);
        Assert.Equal(1, r.Summary.Prefabs);
        Assert.Equal(1, r.Summary.PrefabEntities);
        var inside = Assert.Single(r.Entities, e => e.TargetName == "inside_left");
        Assert.Single(r.Entities, e => e.TargetName == "box1" && e.ClassName == "worldspawn"); // the named prefab as an I/O target
        Assert.True(inside.Id >= VmapMap.PrefabIdBase);
        Assert.Equal("maps/prefabs/box.vmap", inside.KeyValues["prefab"]);
        Assert.True(VmfEntities.SameOrigin(new Vector3(100, 10, 0), inside.Origin!.Value), inside.KeyValues["origin"]);
        Assert.Single(r.Entities, e => e.Id == 2);
        Assert.Equal(0, VmapMap.Read(path, expandPrefabs: false).Summary.PrefabEntities);
    }
}
