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
}
