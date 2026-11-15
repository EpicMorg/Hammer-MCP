namespace HammerMcp.Core.Assets;

/// <summary>Looks up content (models/x.mdl, materials/x.vmt, ...) in a game's "game" search paths: loose folders and VPKs (GoldSrc / Quake: PAKs).</summary>
public sealed class AssetResolver
{
    /// <summary>A loose folder or a pack (VPK / PAK), in search order.</summary>
    private sealed record Mount(string? Folder, VpkDirectory? Pack);

    private readonly List<Mount> _mounts = [];

    public IReadOnlyList<string> Folders => [.. _mounts.Where(m => m.Folder is not null).Select(m => m.Folder!)];
    public IReadOnlyList<string> Vpks => [.. _mounts.Where(m => m.Pack is not null).Select(m => m.Pack!.Path)];
    public List<string> Problems { get; } = [];

    /// <summary>
    /// The "game" search paths of gameinfo.txt in their order (a VPK listed before a folder wins over its loose files).
    /// Left 4 Dead and later (Portal 2, CS:GO, ...) list no VPKs and mount pak01_dir.vpk of each search folder, ahead of
    /// the folder's loose files; Source 2013 games list their VPKs, so an unlisted pak01_dir.vpk is not mounted there.
    /// </summary>
    public AssetResolver(GameInfo info)
    {
        var autoPak01 = !info.SearchPaths.Any(sp => sp.Value.Contains(".vpk", StringComparison.OrdinalIgnoreCase));
        foreach (var sp in info.SearchPaths.Where(s => s.IsGame))
        {
            // an absent custom/* folder is normal; only report other paths that resolve to nothing (e.g. unknown |appid_N|)
            if (sp.Locations.Count == 0 && !sp.Value.TrimEnd().EndsWith('*'))
                Problems.Add($"search path not resolved: {sp.Value}");
            foreach (var location in sp.Locations)
            {
                var pak01 = Path.Combine(location, "pak01_dir.vpk");
                if (autoPak01 && Directory.Exists(location) && File.Exists(pak01))
                    Add(pak01);
                Add(location);
            }
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
        if (location.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                AddPack(PakFile.Open(location));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
            {
                Problems.Add($"{location}: {e.Message}");
            }
        }
        else if (location.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (VpkDirectory.Open(location) is { } vpk)
                    AddPack(vpk);
                else
                    Problems.Add($"vpk not found: {location}");
            }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException)
            {
                Problems.Add($"{location}: {e.Message}");
            }
        }
        else if (Directory.Exists(location))
        {
            if (!_mounts.Any(m => m.Folder is not null && SamePath(m.Folder, location)))
                _mounts.Add(new Mount(location, null));
        }
        else if (!Path.GetFileName(location.TrimEnd('/', '\\')).Equals("download", StringComparison.OrdinalIgnoreCase))
            Problems.Add($"folder not found: {location}"); // download/ appears only after joining a server
    }

    /// <summary>A pack listed twice (or mounted automatically and listed too) is searched once, at its first place.</summary>
    private void AddPack(VpkDirectory pack)
    {
        if (!_mounts.Any(m => m.Pack is not null && SamePath(m.Pack.Path, pack.Path)))
            _mounts.Add(new Mount(null, pack));
    }

    private static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).TrimEnd('/', '\\').Equals(Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every file under <paramref name="root"/> (e.g. "models", "" = all) with where it comes from, in search order - the
    /// order <see cref="Locate"/> uses, so the first source of a path is the copy that wins.
    /// Paths are relative, lower-case for VPKs, with forward slashes.
    /// </summary>
    public IEnumerable<(string Path, string Source)> List(string root)
    {
        var prefix = VpkDirectory.Normalize(root).TrimEnd('/');
        foreach (var mount in _mounts)
        {
            if (mount.Folder is { } folder)
            {
                var dir = prefix.Length == 0 ? folder : Path.Combine(folder, prefix);
                if (!Directory.Exists(dir))
                    continue;
                var files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
                foreach (var file in files)
                    yield return (Path.GetRelativePath(folder, file).Replace('\\', '/'), folder);
            }
            else
            {
                foreach (var file in mount.Pack!.Files)
                {
                    if (prefix.Length == 0 || (file.StartsWith(prefix, StringComparison.Ordinal) && file.Length > prefix.Length && file[prefix.Length] == '/'))
                        yield return (file, mount.Pack.Path);
                }
            }
        }
    }

    /// <summary>True if the file exists in any game search path (path relative to the game root, any slashes, any case in VPKs).</summary>
    public bool Exists(string relativePath) => Locate(relativePath) is not null;

    /// <summary>The bytes of the first copy of a file in search order and where it came from; null if missing.</summary>
    public (byte[] Data, string Source)? Read(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var mount in _mounts)
        {
            if (mount.Folder is { } folder)
            {
                var full = Path.Combine(folder, rel);
                if (!File.Exists(full))
                    continue;
                try
                {
                    return (File.ReadAllBytes(full), full);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // locked or unreadable: the engine would not get it either, try the next copy
                }
            }
            else if (mount.Pack!.Contains(rel) && mount.Pack.ReadFile(rel) is { } data)
                return (data, mount.Pack.Path);
        }
        return null;
    }

    /// <summary>Where the file comes from: the loose file or the VPK path, or null if missing.</summary>
    public string? Locate(string relativePath)
    {
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var mount in _mounts)
        {
            if (mount.Folder is { } folder)
            {
                var full = Path.Combine(folder, rel);
                if (File.Exists(full))
                    return full;
            }
            else if (mount.Pack!.Contains(rel))
                return mount.Pack.Path;
        }
        return null;
    }
}
