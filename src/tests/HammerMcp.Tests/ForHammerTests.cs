using HammerMcp.Core.Config;

namespace HammerMcp.Tests;

public class ForHammerTests
{
    private static HammerGameConfig Config(string name, string gameDir) =>
        new(name, [], gameDir, null, null, null, new Dictionary<string, string>());

    [Fact]
    public void PicksTheInstallsOwnGame()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammermcp-tf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "hl2"));
        Directory.CreateDirectory(Path.Combine(root, "tf"));
        File.WriteAllText(Path.Combine(root, "tf", "gameinfo.txt"), "");
        var configs = new[] { Config("Half-Life 2", Path.Combine(root, "hl2")), Config("Team Fortress 2", Path.Combine(root, "tf")) };
        Assert.Equal("Team Fortress 2", HammerGameConfig.ForHammer(configs, Path.Combine(root, "bin", "hammer.exe"))?.Name);
        File.WriteAllText(Path.Combine(root, "hl2", "gameinfo.txt"), "");
        Assert.Null(HammerGameConfig.ForHammer(configs, Path.Combine(root, "bin", "hammer.exe")));
    }
}
