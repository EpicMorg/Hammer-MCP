using ValveKeyValue;

namespace HammerMcp.Core.Assets;

/// <summary>Steam libraries (libraryfolders.vdf) and app install folders (appmanifest_NNN.acf → installdir).</summary>
public sealed class SteamLibrary
{
    private readonly List<string> _libraries = [];

    public IReadOnlyList<string> Libraries => _libraries;

    /// <param name="steamRoot">Steam folder (SteamPath from HKCU\Software\Valve\Steam on Windows).</param>
    public SteamLibrary(string steamRoot)
    {
        _libraries.Add(steamRoot);
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return;
        using var stream = File.OpenRead(vdf);
        var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        foreach (var (_, folder) in doc.Root.Children)
        {
            var path = folder.IsCollection
                ? folder.Children.FirstOrDefault(c => c.Key.Equals("path", StringComparison.OrdinalIgnoreCase)).Value?.ToString()
                : folder.ToString(); // old format: "1" "D:\\SteamLibrary"
            if (!string.IsNullOrEmpty(path) && !_libraries.Contains(path, StringComparer.OrdinalIgnoreCase))
                _libraries.Add(path.Replace(@"\\\\", @"\\"));
        }
    }

    /// <summary>steamapps/common/&lt;installdir&gt; of an installed app, or null.</summary>
    public string? AppFolder(int appId)
    {
        foreach (var lib in _libraries)
        {
            var acf = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(acf))
                continue;
            using var stream = File.OpenRead(acf);
            var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            var installDir = doc.Root.Children.FirstOrDefault(c => c.Key.Equals("installdir", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            if (installDir is not null)
            {
                var folder = Path.Combine(lib, "steamapps", "common", installDir);
                if (Directory.Exists(folder))
                    return folder;
            }
        }
        return null;
    }
}
