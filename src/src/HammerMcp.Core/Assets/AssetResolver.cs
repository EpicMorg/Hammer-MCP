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
            foreach (var location in sp.Locations)
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
                else
                    Problems.Add($"folder not found: {location}");
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
