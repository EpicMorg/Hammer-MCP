using HammerMcp.Core.Assets;
using HammerMcp.Core.Compile;
using HammerMcp.Core.Config;
using HammerMcp.Core.Fgd;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Materials;

namespace HammerMcp.Tests;

/// <summary>
/// Reads every FGD / map / VMT / Hammer game config under $HAMMER_MCP_CORPUS (private, not in this repo).
/// Skipped when the variable is not set.
/// </summary>
public class CorpusTests
{
    private static readonly string? Root = Environment.GetEnvironmentVariable("HAMMER_MCP_CORPUS");

    private static IEnumerable<string> Files(params string[] extensions) =>
        Directory.EnumerateFiles(Root!, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    [Fact]
    public void EveryFgdParsesWithoutCrashing()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        // Source 2 data FGDs (@ModelGameData, @VData) and extension-only FGDs (@ExtendClass) legitimately have no classes of their own
        var empty = Files(".fgd").Where(f => !FgdParser.ParseFile(f).Classes.Any()).ToList();
        Assert.All(empty, f => Assert.True(File.ReadAllText(f).Contains("@ModelGameData") || File.ReadAllText(f).Contains("@VData") || File.ReadAllText(f).Contains("@ExtendClass")
                                          || File.ReadAllText(f).Contains("models_base.fgd"), f));
    }

    [Fact]
    public void EveryMapReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        foreach (var f in Files(".vmf", ".rmf", ".map"))
            MapEntities.Read(f);
    }

    [Fact]
    public void EveryVmapReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        var maps = Files(".vmap").ToList();
        Assert.All(maps, f => Assert.True(VmapMap.Read(f).Summary.EditorBuild > 0, f));
        // Dota tilesets are full of entities with outputs
        Assert.Contains(maps.Select(f => VmapMap.Read(f).Entities), list => list.Any(e => e.Connections.Count > 0));
    }

    [Fact]
    public void EveryVmtAndGameConfigReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        foreach (var f in Files(".vmt"))
            VmtMaterial.Read(f);
        foreach (var f in Directory.EnumerateFiles(Root!, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f) is "GameConfig.txt" or "hammerplusplus_gameconfig.txt"))
            HammerGameConfig.Load(f);
    }

    [Fact]
    public void EveryGameinfoAndVpkDirectoryReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        foreach (var f in Directory.EnumerateFiles(Root!, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f).Equals("gameinfo.txt", StringComparison.OrdinalIgnoreCase)))
            _ = new AssetResolver(GameInfo.Load(f));
        foreach (var f in Directory.EnumerateFiles(Root!, "*_dir.vpk", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length >= 12))
            Assert.NotNull(VpkDirectory.Open(f));
    }

    [Fact]
    public void EveryCompileSequenceAndLogReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        foreach (var f in Directory.EnumerateFiles(Root!, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f).ToLowerInvariant() is "cmdseq.wc" or "cmdseqdefault.wc" or "hammerplusplus_sequences.cfg"))
            Assert.NotEmpty(CompileSequences.Load(f));
        foreach (var f in Files(".log"))
            CompileLog.Read(f);
    }
}
