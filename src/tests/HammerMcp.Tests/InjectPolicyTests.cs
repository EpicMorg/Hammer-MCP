using HammerMcp.Core.Registry;

namespace HammerMcp.Tests;

public class InjectPolicyTests
{
    private static HammerBuild Build(string family, string? adapter) => new()
    {
        Id = "x", Family = family, Branch = "b", Arch = "x64",
        Match = new BuildMatch { Dll = "hammer_dll.dll", Sha256 = "00" }, InjectAdapter = adapter,
    };

    [Theory]
    [InlineData(@"C:\Steam\Half-Life 2\hl2.exe")]
    [InlineData(@"C:\Steam\Team Fortress 2\tf_win64.exe")]
    [InlineData(@"C:\Steam\Counter-Strike Global Offensive\game\bin\win64\cs2.exe")]
    public void NeverInjectsIntoGameProcesses(string exe) =>
        Assert.False(InjectPolicy.Check(exe, Build("valve", "adapter")).Allowed);

    [Fact]
    public void NeverInjectsIntoSource2OrUnknownBuilds()
    {
        Assert.False(InjectPolicy.Check(@"C:\x\hammer.exe", Build("source2", "adapter")).Allowed);
        Assert.False(InjectPolicy.Check(@"C:\x\hammer.exe", null).Allowed);
        Assert.False(InjectPolicy.Check(@"C:\x\hammerplusplus.exe", Build("hammerplusplus", null)).Allowed);
    }

    [Fact]
    public void AllowsStandaloneEditorWithAdapter() =>
        Assert.True(InjectPolicy.Check(@"C:\SDK\bin\hammerplusplus.exe", Build("hammerplusplus", "hpp-8873-x64")).Allowed);

    [Fact]
    public void GoldSrcEditorIsMatchedByItsOwnExe()
    {
        var neon = new HammerBuild
        {
            Id = "goldsrc-neon-hammer-3.5", Family = "goldsrc", Branch = "neon-hammer-3.5", Arch = "x86",
            Match = new BuildMatch { Dll = "neon.exe", Sha256 = "00" }, InjectAdapter = "generic-x86",
        };
        Assert.True(InjectPolicy.Check(@"C:\HL-SDK\neon.exe", neon).Allowed);
        Assert.False(InjectPolicy.Check(@"C:\Steam\Half-Life\hl.exe", neon).Allowed);   // a game exe is never the editor
    }
}
