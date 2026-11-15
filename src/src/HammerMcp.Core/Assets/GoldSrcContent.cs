using HammerMcp.Core.Config;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Content of a GoldSrc game config: loose files of the mod and of the game it falls back to (each with its _addon and
/// _downloads folders, as the engine mounts them), and the texture WADs in those folders. _hd and _&lt;language&gt; folders
/// depend on client settings and are left out. Utility WADs (fonts, gfx, cached, spray / temp decals) are skipped.
/// </summary>
public sealed class GoldSrcContent
{
    private static readonly HashSet<string> NotTextureWads = new(StringComparer.OrdinalIgnoreCase)
    {
        "cached.wad", "fonts.wad", "gfx.wad", "tempdecal.wad", "spraypaint.wad", "pldecal.wad",
    };

    public AssetResolver Files { get; }
    public IReadOnlyList<WadDirectory> Wads { get; }
    public IReadOnlyList<string> Folders { get; }
    public List<string> Problems { get; } = [];

    private GoldSrcContent(IReadOnlyList<string> folders, IReadOnlyList<WadDirectory> wads)
    {
        Folders = folders;
        Files = new AssetResolver(folders);
        Wads = wads;
    }

    /// <summary>Folders in mount order: mod_addon, mod, mod_downloads, then the same for the base game (valve).</summary>
    public static IReadOnlyList<string> SearchFolders(HammerGameConfig config)
    {
        var result = new List<string>();
        foreach (var dir in new[] { config.GameDir, config.BaseGameDir })
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var root = dir.TrimEnd('\\', '/');
            foreach (var folder in new[] { root + "_addon", root, root + "_downloads" })
            {
                if (Directory.Exists(folder) && !result.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    result.Add(folder);
            }
        }
        return result;
    }

    public static GoldSrcContent ForConfig(HammerGameConfig config)
    {
        var folders = SearchFolders(config);
        var wads = new List<WadDirectory>();
        var problems = new List<string>();
        foreach (var folder in folders)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.wad").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (NotTextureWads.Contains(Path.GetFileName(file)))
                    continue;
                try
                {
                    wads.Add(WadDirectory.Open(file));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
                {
                    problems.Add($"{file}: {e.Message}");
                }
            }
        }
        var content = new GoldSrcContent(folders, wads);
        foreach (var dir in new[] { config.GameDir, config.BaseGameDir })
        {
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                content.Problems.Add($"folder not found: {dir}");
        }
        content.Problems.AddRange(problems);
        return content;
    }
}
