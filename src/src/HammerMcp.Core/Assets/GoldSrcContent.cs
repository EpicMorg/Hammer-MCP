using HammerMcp.Core.Config;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Content of a GoldSrc game config: loose files and PAKs (pak0.pak, pak1.pak, ...) of the mod and of the game it falls
/// back to (each with its _addon and _downloads folders, as the engine mounts them), and texture WADs: the ones the
/// config lists (J.A.C.K.), then those in the folders. _hd and _&lt;language&gt; folders
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
        Files = new AssetResolver(folders.Concat(folders.SelectMany(Paks)));
        Wads = wads;
        Problems.AddRange(Files.Problems);
    }

    /// <summary>pak0.pak, pak1.pak, ... of a folder, in number order.</summary>
    public static IEnumerable<string> Paks(string folder) =>
        Directory.EnumerateFiles(folder, "*.pak")
            .Select(f => (File: f, Name: Path.GetFileNameWithoutExtension(f)))
            .Select(p => (p.File, N: p.Name.StartsWith("pak", StringComparison.OrdinalIgnoreCase) && int.TryParse(p.Name.AsSpan(3), out var n) ? n : int.MaxValue))
            .OrderBy(p => p.N).ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.File);

    /// <summary>
    /// The game folder a config without Mod / Game directories (GameCfg.wc 1.3: Worldcraft 1.6-2.x) most likely means:
    /// &lt;game exe dir&gt;\valve (Half-Life) or \id1 (Quake), if it exists. A guess - reported as such.
    /// </summary>
    public static string? GuessGameDir(HammerGameConfig config)
    {
        if (config.GameDir is not null || config.BaseGameDir is not null || string.IsNullOrWhiteSpace(config.GameExeDir))
            return null;
        return new[] { "valve", "id1" }.Select(d => Path.Combine(config.GameExeDir, d)).FirstOrDefault(Directory.Exists);
    }

    /// <summary>Folders in mount order: mod_addon, mod, mod_downloads, then the same for the base game (valve).</summary>
    public static IReadOnlyList<string> SearchFolders(HammerGameConfig config)
    {
        var result = new List<string>();
        foreach (var dir in new[] { config.GameDir, config.BaseGameDir, GuessGameDir(config) })
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = (config.TextureWads ?? []).Select(w => w.Replace('/', '\\')).ToList();
        foreach (var missing in listed.Where(w => !File.Exists(w)))
            problems.Add($"texture WAD listed by the editor not found: {missing}");
        var files = listed.Where(File.Exists)
            .Concat(folders.SelectMany(f => Directory.EnumerateFiles(f, "*.wad").Order(StringComparer.OrdinalIgnoreCase)
                .Where(w => !NotTextureWads.Contains(Path.GetFileName(w)))));
        foreach (var file in files)
        {
            if (!seen.Add(Path.GetFullPath(file)))
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
        var content = new GoldSrcContent(folders, wads);
        foreach (var dir in new[] { config.GameDir, config.BaseGameDir })
        {
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                content.Problems.Add($"folder not found: {dir}");
        }
        if (GuessGameDir(config) is { } guess)
            content.Problems.Add($"the game config has no Mod / Game directory (old GameCfg.wc); guessed {guess} from the game exe folder");
        else if (folders.Count == 0 && config.GameDir is null && config.BaseGameDir is null)
            content.Problems.Add("the game config has no Mod / Game directory and none could be guessed from the game exe folder: only the editor's texture WADs are searched");
        content.Problems.AddRange(problems);
        return content;
    }
}
