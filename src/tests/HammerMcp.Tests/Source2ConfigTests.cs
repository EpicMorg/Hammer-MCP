using HammerMcp.Core.Config;

namespace HammerMcp.Tests;

public class Source2ConfigTests
{
    [Fact]
    public void FindsModsWithTheirFgd()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammermcp-s2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "game", "bin", "win64", "tools"));
        File.WriteAllText(Path.Combine(root, "game", "bin", "win64", "tools", "hammer.dll"), "");
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo"));
        File.WriteAllText(Path.Combine(root, "game", "csgo", "csgo.fgd"), "");
        Directory.CreateDirectory(Path.Combine(root, "game", "core"));
        File.WriteAllText(Path.Combine(root, "game", "core", "core.fgd"), "");

        var configs = HammerGameConfig.Source2(Path.Combine(root, "game", "bin", "win64", "cs2.exe"));
        var c = Assert.Single(configs);
        Assert.Equal("csgo", c.Name);
        Assert.EndsWith(Path.Combine("game", "csgo", "csgo.fgd"), Assert.Single(c.FgdFiles));
        Assert.Empty(HammerGameConfig.Source2(Path.Combine(root, "game", "csgo", "x.exe")));
    }

    [Theory]
    [InlineData(@"C:\cs2\content\csgo_addons\my addon\maps\de_x.vmap", "csgo")]
    [InlineData(@"C:\dota 2 beta\content\dota\maps\dota.vmap", "dota")]
    [InlineData(@"C:\maps\x.vmf", null)]
    public void ModOfMap(string path, string? mod) => Assert.Equal(mod, HammerGameConfig.Source2ModOf(path));

    [Fact]
    public void FindsMapByNameInContentAndAddons()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammermcp-s2c-{Guid.NewGuid():N}");
        var bin = Path.Combine(root, "game", "bin", "win64");
        Directory.CreateDirectory(Path.Combine(bin, "tools"));
        File.WriteAllText(Path.Combine(bin, "tools", "hammer.dll"), "");
        var stock = Path.Combine(root, "content", "csgo", "maps", "demo", "cs_script_demo.vmap");
        var addon = Path.Combine(root, "content", "csgo_addons", "my addon", "maps", "de_mine.vmap");
        Directory.CreateDirectory(Path.GetDirectoryName(stock)!);
        Directory.CreateDirectory(Path.GetDirectoryName(addon)!);
        File.WriteAllText(stock, "");
        File.WriteAllText(addon, "");
        var exe = Path.Combine(bin, "cs2.exe");
        Assert.Equal(stock, HammerMcp.Core.Maps.Source2Content.FindMap(exe, "cs_script_demo.vmap"));
        Assert.Equal(addon, HammerMcp.Core.Maps.Source2Content.FindMap(exe, "DE_MINE.vmap"));
        Assert.Equal(addon, HammerMcp.Core.Maps.Source2Content.FindMap(exe, "maps/de_mine.vmap"));
        Assert.Null(HammerMcp.Core.Maps.Source2Content.FindMap(exe, "nope.vmap"));
    }

    [Fact]
    public void FindsSource2AutosavesInTheToolsAutosaveTree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammermcp-s2a-{Guid.NewGuid():N}");
        var map = Path.Combine(root, "content", "csgo_addons", "x", "maps", "demo.vmap");
        Directory.CreateDirectory(Path.GetDirectoryName(map)!);
        File.WriteAllText(map, "");
        File.SetLastWriteTimeUtc(map, DateTime.UtcNow.AddMinutes(-5));
        var folder = HammerMcp.Core.Maps.Source2Content.AutosaveFolder(map)!;
        Assert.Equal(Path.Combine(root, "content", "_toolsautosave", "hammer", "csgo_addons", "x", "maps"), folder);
        Directory.CreateDirectory(folder);
        var older = Path.Combine(folder, "demo001.vmap_autosave");
        var newer = Path.Combine(folder, "demo002.vmap_autosave");
        var otherMap = Path.Combine(folder, "demo_v2003.vmap_autosave");
        File.WriteAllText(older, "");
        File.WriteAllText(newer, "");
        File.WriteAllText(otherMap, "");
        File.SetLastWriteTimeUtc(otherMap, DateTime.UtcNow.AddMinutes(1));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(newer, HammerMcp.Core.Maps.Source2Content.NewestAutosave(map));
        Assert.Equal(map, HammerMcp.Core.Maps.Source2Content.FromAutosaveTree(Path.Combine(folder, "demo.vmap")));
    }
}
