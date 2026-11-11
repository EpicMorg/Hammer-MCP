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
