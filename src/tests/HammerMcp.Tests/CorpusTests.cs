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
        foreach (var f in Files(".fgd"))
            Assert.NotEmpty(FgdParser.ParseFile(f).Classes);
    }

    [Fact]
    public void EveryMapReads()
    {
        Assert.SkipWhen(Root is null, "HAMMER_MCP_CORPUS not set");
        foreach (var f in Files(".vmf", ".rmf", ".map"))
            MapEntities.Read(f);
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
