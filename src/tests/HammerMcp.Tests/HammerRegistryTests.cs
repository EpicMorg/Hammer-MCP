using HammerMcp.Core.Registry;

namespace HammerMcp.Tests;

public class HammerRegistryTests
{
    private static readonly string RegistryPath = Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json");

    [Fact]
    public void ShippedRegistryLoads()
    {
        var registry = HammerRegistry.Load(RegistryPath);
        Assert.NotEmpty(registry.Builds);
        Assert.All(registry.Builds, b => Assert.Equal(64, b.Match.Sha256.Length));
    }

    [Fact]
    public void FindsBuildBySha256CaseInsensitive()
    {
        var registry = HammerRegistry.Load(RegistryPath);
        var hpp = registry.Builds.First(b => b.Id == "hpp-tf2-8873-x64");
        Assert.Same(hpp, registry.FindBySha256(hpp.Match.Sha256.ToUpperInvariant()));
    }

    [Fact]
    public void LiveMemoryLayoutsAreComplete()
    {
        // the adapter turns a feature off when a key is missing (see src/native/adapters/base/live.cpp); a layout in the
        // registry must have them all, and every value must be a number
        string[] selection = ["activeDoc", "doc.selection", "object.mins", "object.maxs", "object.origin", "object.parent",
            "entity.vtable", "entity.className", "entity.keyValues", "entity.keyValueCount", "entity.keyValueSize", "entity.keyLength",
            "entity.valueOffset", "entity.valueLength", "solid.vtable", "solid.faces", "face.size", "face.texture"];
        string[] source = ["activeDoc", "doc.selection", "selection.list", "object.mins", "object.maxs", "object.origin", "object.parent",
            "entity.vtable", "entity.className", "entity.keyValues", "kv.nodes", "kv.alloc", "kv.root", "kv.nodeSize", "kv.left", "kv.right",
            "kv.key", "kv.keyLength", "kv.value", "kv.valueLength", "solid.vtable", "solid.faces", "solid.faceCount", "face.size", "face.texture"];
        string[] camera = ["doc.views", "view3d.vtable", "view3d.camera", "camera.position"];
        var layouts = HammerRegistry.Load(RegistryPath).Builds.Where(b => b.Memory.Count > 0).ToList();
        Assert.Contains(layouts, b => b.Id == "goldsrc-hammer-3.5");
        foreach (var b in layouts)
        {
            // GoldSrc layouts read the selection from MFC lists; "layout": "source" ones from CSelection / CUtlVector / CUtlDict
            var isSource = b.Memory.TryGetValue("layout", out var kind) && kind == "source";
            Assert.All(isSource ? source : selection, k => Assert.True(b.Memory.ContainsKey(k), $"{b.Id}: no {k}"));
            // the camera is optional for Source layouts (found live, not statically), but all or nothing
            if (!isSource || camera.Any(b.Memory.ContainsKey))
            {
                Assert.All(camera, k => Assert.True(b.Memory.ContainsKey(k), $"{b.Id}: no {k}"));
                Assert.True(b.Memory.ContainsKey("camera.matrix") || b.Memory.ContainsKey("camera.forward"), $"{b.Id}: no camera direction");
            }
            Assert.All(b.Memory, kv => Assert.True(
                kv.Key is "module" or "layout" || kv.Value.StartsWith("0x", StringComparison.Ordinal) &&
                ulong.TryParse(kv.Value[2..], System.Globalization.NumberStyles.HexNumber, null, out _), $"{b.Id}: {kv.Key} = {kv.Value}"));
        }
    }

    [Fact]
    public void EveryGoldSrcEditorHasItsSettingsKey()
    {
        // found by running each editor (2026-11-15): where Options > Textures keeps the texture WAD list
        var builds = HammerRegistry.Load(RegistryPath).Builds.Where(b => b.Family == "goldsrc").ToList();
        Assert.Equal(14, builds.Count);
        Assert.All(builds, b => Assert.StartsWith(@"Software\", b.SettingsKey));
        Assert.Equal(@"Software\Worldcraft\Worldcraft", builds.Single(b => b.Id == "goldsrc-worldcraft-2.0").SettingsKey);
        Assert.Equal(@"Software\Valve\Worldcraft", builds.Single(b => b.Id == "goldsrc-worldcraft-2.1").SettingsKey);
        Assert.Equal(@"Software\Valve\Valve Hammer Editor Neon", builds.Single(b => b.Id == "goldsrc-neon-hammer-3.5-rus").SettingsKey);
        Assert.Contains(builds, b => b.Id == "goldsrc-hammer-3.5-b");
    }

    [Fact]
    public void EmbeddedRegistryMatchesTheFile() =>
        Assert.Equal(HammerRegistry.Load(RegistryPath).Builds.Count, HammerRegistry.LoadDefault().Builds.Count);

    [Fact]
    public void RejectsDuplicateIds()
    {
        const string json = """
            { "builds": [
              { "id": "a", "family": "valve", "branch": "sp", "arch": "x86", "match": { "dll": "hammer_dll.dll", "sha256": "00" } },
              { "id": "a", "family": "valve", "branch": "sp", "arch": "x86", "match": { "dll": "hammer_dll.dll", "sha256": "11" } }
            ] }
            """;
        Assert.Throws<InvalidDataException>(() => HammerRegistry.Parse(json));
    }
}
