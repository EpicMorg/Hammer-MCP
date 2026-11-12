using ValveKeyValue;

namespace HammerMcp.Core.Materials;

/// <summary>A .vmt material: shader and its parameters ($basetexture, $surfaceprop, ...), nested blocks (Proxies, patch include...) as text.</summary>
public sealed record VmtMaterial(string Shader, IReadOnlyDictionary<string, string> Parameters, IReadOnlyDictionary<string, string> Blocks)
{
    /// <summary>For "patch" materials: the material it patches ("include").</summary>
    public string? Include => Parameters.GetValueOrDefault("include");

    public string? BaseTexture => Parameters.GetValueOrDefault("$basetexture");

    public static VmtMaterial Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static VmtMaterial Parse(Stream stream)
    {
        var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in doc.Root.Children)
        {
            if (value.IsCollection)
                blocks[name] = Flatten(value); // Proxies, patch insert / replace, ...
            else
                parameters[name] = value.ToString() ?? "";
        }
        return new VmtMaterial(doc.Name ?? "", parameters, blocks);
    }

    private static string Flatten(KVObject obj) =>
        string.Join("; ", obj.Children.Select(c => c.Value.IsCollection ? $"{c.Key} {{ {Flatten(c.Value)} }}" : $"{c.Key} {c.Value}"));
}
