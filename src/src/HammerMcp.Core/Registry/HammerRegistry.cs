using System.Text.Json;
using System.Text.Json.Serialization;

namespace HammerMcp.Core.Registry;

/// <summary>Known Hammer builds loaded from registry/hammers.json.</summary>
public sealed class HammerRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public IReadOnlyList<HammerBuild> Builds { get; }

    public HammerRegistry(IEnumerable<HammerBuild> builds)
    {
        Builds = [.. builds];
        var duplicate = Builds.GroupBy(b => b.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"duplicate registry id '{duplicate.Key}'");
    }

    public static HammerRegistry Load(string path) => Parse(File.ReadAllText(path));

    public static HammerRegistry Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<RegistryFile>(json, JsonOptions)
                  ?? throw new InvalidDataException("empty registry");
        return new HammerRegistry(doc.Builds);
    }

    /// <summary>Finds the build whose editor DLL has this sha256 (case-insensitive hex).</summary>
    public HammerBuild? FindBySha256(string sha256) =>
        Builds.FirstOrDefault(b => string.Equals(b.Match.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

    private sealed record RegistryFile(List<HammerBuild> Builds);
}
