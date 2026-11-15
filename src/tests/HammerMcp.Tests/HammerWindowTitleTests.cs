using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class HammerWindowTitleTests
{
    [Theory]
    [InlineData(@"Hammer++ - 64 Bit - [B:\SteamLibrary\steamapps\common\Team Fortress 2 Classified\sdk_content\mapsrc\mcp_smoke.vmf - Textured Shaded]",
                @"B:\SteamLibrary\steamapps\common\Team Fortress 2 Classified\sdk_content\mapsrc\mcp_smoke.vmf")]
    [InlineData(@"Hammer - [C:\maps\d1_test.vmf]", @"C:\maps\d1_test.vmf")]
    [InlineData(@"Hammer - de_test.vmap* - C:\Steam\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\my addon\maps\de_test.vmap", @"C:\Steam\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\my addon\maps\de_test.vmap")]
    [InlineData(@"Valve Hammer Editor - [C:\maps\crossfire.rmf - 3D Textures]", @"C:\maps\crossfire.rmf")]
    // Hammer 3.x with the map window not maximized: only the file name (resolved from the command line or MapDir)
    [InlineData("Valve Hammer Editor - tut1.rmf", "tut1.rmf")]
    [InlineData("Valve Hammer Editor - my map.map *", "my map.map")]
    public void ParsesMapPath(string title, string expected) => Assert.Equal(expected, HammerWindowTitle.ParseMapPath(title));

    [Theory]
    [InlineData("Hammer++ - 64 Bit")]
    [InlineData("Notepad++")]
    [InlineData("Valve Hammer Editor")]
    [InlineData("readme.txt - Notepad")]
    public void NoMapInTitle(string title) => Assert.Null(HammerWindowTitle.ParseMapPath(title));
}
