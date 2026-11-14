namespace HammerMcp.Core.Assets;

/// <summary>Looks up content (models/x.mdl, materials/x.vmt, ...) in a game's "game" search paths: loose folders and VPKs.</summary>
public sealed class AssetResolver
{
    private readonly List<string> _folders = [];
    private readonly List<VpkDirectory> _vpks = [];

    public IReadOnlyList<string> Folders => _folders;
    public IReadOnlyList<string> Vpks => _vpks.Select(v => v.Path).ToList();
    public List<string> Problems { get; } = [];

    public AssetResolver(GameInfo info)
    {
        foreach (var sp in info.SearchPaths.Where(s => s.IsGame))
        {
            // an absent custom/* folder is normal; only report other paths that resolve to nothing (e.g. unknown |appid_N|)
            if (sp.Locations.Count == 0 && !sp.Value.TrimEnd().EndsWith('*'))
                Problems.Add($"search path not resolved: {sp.Value}");
            foreach (var location in sp.Locations)
                Add(location);
        }
    }

    /// <summary>Folders and VPKs in mount order (e.g. Source 2: game/&lt;mod&gt;, its pak01_dir.vpk, game/core, ...).</summary>
    public AssetResolver(IEnumerable<string> locations)
    {
        foreach (var location in locations)
            Add(location);
    }

    private void Add(string location)
    {
        if (location.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (VpkDirectory.Open(location) is { } vpk)
                    _vpks.Add(vpk);
                else
                    Problems.Add($"vpk not found: {location}");
            }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException)
            {
                Problems.Add($"{location}: {e.Message}");
            }
        }
        else if (Directory.Exists(location))
            _folders.Add(location);
        else if (!Path.GetFileName(location.TrimEnd('/', '\\')).Equals("download", StringComparison.OrdinalIgnoreCase))
            Problems.Add($"folder not found: {location}"); // download/ appears only after joining a server
    }

    /// <summary>
    /// Every file under <paramref name="root"/> (e.g. "models", "" = all) with where it comes from: loose folders first,
    /// then VPKs - the order <see cref="Locate"/> uses, so the first source of a path is the copy that wins.
    /// Paths are relative, lower-case for VPKs, with forward slashes.
    /// </summary>
    public IEnumerable<(string Path, string Source)> List(string root)
    {
        var prefix = VpkDirectory.Normalize(root).TrimEnd('/');
        foreach (var folder in _folders)
        {
            var dir = prefix.Length == 0 ? folder : Path.Combine(folder, prefix);
            if (!Directory.Exists(dir))
                continue;
            var files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            foreach (var file in files)
                yield return (Path.GetRelativePath(folder, file).Replace('\\', '/'), folder);
        }
        foreach (var vpk in _vpks)
        {
            foreach (var file in vpk.Files)
            {
                if (prefix.Length == 0 || (file.StartsWith(prefix, StringComparison.Ordinal) && file.Length > prefix.Length && file[prefix.Length] == '/'))
                    yield return (file, vpk.Path);
            }
        }
    }

    /// <summary>True if the file exists in any game search path (path relative to the game root, any slashes, any case in VPKs).</summary>
    public bool Exists(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var folder in _folders)
        {
            if (File.Exists(Path.Combine(folder, rel)))
                return true;
        }
        return _vpks.Any(v => v.Contains(rel));
    }

    /// <summary>Where the file comes from: a folder or a VPK path, or null if missing.</summary>
    public string? Locate(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var folder in _folders)
        {
            var full = Path.Combine(folder, rel);
            if (File.Exists(full))
                return full;
        }
        return _vpks.FirstOrDefault(v => v.Contains(rel))?.Path;
    }
}
