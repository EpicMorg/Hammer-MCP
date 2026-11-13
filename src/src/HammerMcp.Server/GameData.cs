using System.Runtime.Versioning;
using HammerMcp.Core.Assets;
using HammerMcp.Core.Config;
using HammerMcp.Core.Fgd;

namespace HammerMcp.Server;

/// <summary>
/// Game configuration and FGDs of the attached Hammer, exactly as Hammer itself sees them (GameConfig.txt /
/// hammerplusplus_gameconfig.txt, GameData0..N). Parsed FGDs are cached until a file changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameData(HammerTarget target)
{
    private readonly Lock _lock = new();
    private string? _cacheKey;
    private FgdDocument? _cache;
    private string? _assetsKey;
    private AssetResolver? _assets;

    public sealed record Selection(string? ConfigFile, IReadOnlyList<HammerGameConfig> Configs, HammerGameConfig? Active, string? Problem);

    /// <summary>Game configs of the attached Hammer and the one that applies to the open map (or <paramref name="configName"/>).</summary>
    public Selection Select(string? configName = null)
    {
        var process = target.Current();
        if (process is null)
            return new Selection(null, [], null, "no Hammer is attached");
        var (file, configs) = HammerGameConfig.ForExe(process.ExePath);
        if (file is null)
            return new Selection(null, [], null, $"no GameConfig.txt / hammerplusplus_gameconfig.txt next to {process.ExePath}");
        var active = configName is not null
            ? configs.FirstOrDefault(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase))
            : HammerGameConfig.ForMap(configs, process.OpenMapPath);
        var problem = active is null
            ? configName is not null
                ? $"no game config named '{configName}'"
                : "several game configs and the open map is not under any MapDir: pass configName (see game_configs)"
            : null;
        return new Selection(file, configs, active, problem);
    }

    /// <summary>
    /// Content lookup for a config: GameDir\gameinfo.txt search paths; |all_source_engine_paths| = GameExeDir (the folder of
    /// the game exe, e.g. ...\Half-Life 2). Rebuilt when gameinfo.txt changes.
    /// </summary>
    public AssetResolver? Assets(HammerGameConfig config)
    {
        if (config.GameDir is null)
            return null;
        var gameinfo = Path.Combine(config.GameDir, "gameinfo.txt");
        if (!File.Exists(gameinfo))
            return null;
        var root = config.GameExeDir is { Length: > 0 } exeDir ? exeDir
            : config.Tools.TryGetValue("GameExe", out var exe) && exe.Length > 0 ? Path.GetDirectoryName(exe) : null;
        var key = $"{gameinfo}@{File.GetLastWriteTimeUtc(gameinfo).Ticks}@{root}";
        lock (_lock)
        {
            if (_assetsKey != key || _assets is null)
            {
                var steam = SteamRoot() is { } steamRoot ? new SteamLibrary(steamRoot) : null;
                _assets = new AssetResolver(GameInfo.Load(gameinfo, root, steam is null ? null : steam.AppFolder));
                _assetsKey = key;
            }
            return _assets;
        }
    }

    /// <summary>Steam folder from HKCU\Software\Valve\Steam\SteamPath (for |appid_NNN| search paths).</summary>
    private static string? SteamRoot() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p && Directory.Exists(p) ? p : null;

    /// <summary>FGDs of a config, merged in Hammer's order.</summary>
    public FgdDocument Fgd(HammerGameConfig config)
    {
        var key = string.Join("|", config.FgdFiles.Select(f => $"{f}@{(File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks : 0)}"));
        lock (_lock)
        {
            if (_cacheKey != key || _cache is null)
            {
                _cache = FgdLoader.LoadGame(config.FgdFiles);
                _cacheKey = key;
            }
            return _cache;
        }
    }
}
