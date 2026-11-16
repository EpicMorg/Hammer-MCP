using HammerMcp.Core.Registry;

namespace HammerMcp.Tests;

public class UiKnowledgeTests
{
    [Fact]
    public void EveryUiFileBelongsToARegistryBuild()
    {
        var registry = HammerRegistry.Load(Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json"));
        Assert.Contains("goldsrc-hammer-3.5", UiKnowledge.Known());
        Assert.All(UiKnowledge.Known(), id => Assert.Contains(registry.Builds, b => b.Id == id));
    }

    [Fact]
    public void FindsTheHollowRecipeAndDialog()
    {
        var ui = UiKnowledge.For("goldsrc-hammer-3.5")!;
        var found = ui.Search("hollow");
        Assert.NotNull(found["recipes"]?["make_hollow"]);
        Assert.Contains("1009", found["recipes"]!["make_hollow"]!.ToJsonString());
        Assert.Contains("0x8098", found["menus"]!.ToJsonString());
    }

    [Fact]
    public void ABuildWithTheSameUiUsesTheVerifiedRecipes()
    {
        var ui = UiKnowledge.For("goldsrc-hammer-3.4")!;
        Assert.Equal("goldsrc-hammer-3.5", ui.RecipesBuild);
        Assert.Equal("goldsrc-hammer-3.5", (string?)ui.Search("transform")["recipesVerifiedOn"]);
        Assert.Contains("make_hollow", ui.RecipeNames);
    }

    [Fact]
    public void UiFilesArePublicDumps()
    {
        // the repository gets ids / classes / keys / short labels only (dump_ui.py --public), never Valve's prose
        foreach (var id in UiKnowledge.Known())
        {
            var json = UiKnowledge.For(id)!.ToJson();
            Assert.Contains("\"public\":true", json);
            Assert.DoesNotContain("\"status\":", json);
            Assert.DoesNotContain("Copyright", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("trademark", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SharedUiFilesResolveToAnExistingFile()
    {
        var files = typeof(UiKnowledge).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("ui/", StringComparison.Ordinal)).ToHashSet();
        Assert.NotEmpty(UiKnowledge.SameAs);
        Assert.All(UiKnowledge.SameAs, kv =>
        {
            Assert.Contains($"ui/{kv.Value}.json", files);
            Assert.DoesNotContain($"ui/{kv.Key}.json", files); // a build has its own file or shares one, not both
            Assert.NotNull(UiKnowledge.For(kv.Key));
        });
    }

    [Fact]
    public void BuildsWithTheCodeOf35UseItsRecipes()
    {
        foreach (var id in new[] { "goldsrc-neon-hammer-3.5", "goldsrc-neo-hammer-3.5.1", "goldsrc-csm-hammer-3.5" })
            Assert.Equal("goldsrc-hammer-3.5", UiKnowledge.For(id)!.RecipesBuild);
    }

    [Fact]
    public void UnknownBuildHasNoKnowledge() => Assert.Null(UiKnowledge.For("no-such-build"));
}
