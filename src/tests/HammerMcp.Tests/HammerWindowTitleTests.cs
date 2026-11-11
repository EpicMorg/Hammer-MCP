using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class HammerWindowTitleTests
{
    [Theory]
    [InlineData(@"Hammer++ - 64 Bit - [B:\SteamLibrary\steamapps\common\Team Fortress 2 Classified\sdk_content\mapsrc\mcp_smoke.vmf - Textured Shaded]",
                @"B:\SteamLibrary\steamapps\common\Team Fortress 2 Classified\sdk_content\mapsrc\mcp_smoke.vmf")]
    [InlineData(@"Hammer - [C:\maps\d1_test.vmf]", @"C:\maps\d1_test.vmf")]
    [InlineData(@"Valve Hammer Editor - [C:\maps\crossfire.rmf - 3D Textures]", @"C:\maps\crossfire.rmf")]
    public void ParsesMapPath(string title, string expected) => Assert.Equal(expected, HammerWindowTitle.ParseMapPath(title));

    [Theory]
    [InlineData("Hammer++ - 64 Bit")]
    [InlineData("Notepad++")]
    public void NoMapInTitle(string title) => Assert.Null(HammerWindowTitle.ParseMapPath(title));
}
