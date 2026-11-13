using System.Runtime.Versioning;
using HammerMcp.Shell;

namespace HammerMcp.Server;

/// <summary>
/// The Hammer process the server works with: attached by the user in the app. Nothing attached = no target
/// (the tools then tell the agent to ask the user to attach a Hammer process).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HammerTarget
{
    private int? _attachedPid;

    public int? AttachedPid => _attachedPid;

    public void Attach(int pid) => _attachedPid = pid;

    public void Detach() => _attachedPid = null;

    /// <summary>Fresh info about the target (windows, open map); null if it is not running.</summary>
    public HammerProcess? Current() =>
        _attachedPid is { } pid ? HammerProcess.Refresh(pid) : null;

    /// <summary>
    /// The file to read the open map from: the saved .vmf, or - with <paramref name="fromAutosave"/> - Hammer++'s newest
    /// autosave of it if that is newer (fresher than the last Ctrl+S). Null path = no saved map open.
    /// </summary>
    public (string? Path, string Source) MapSource(bool fromAutosave)
    {
        var process = Current();
        var map = process?.OpenMapPath;
        if (process is null || map is null || !File.Exists(map))
            return (null, "none");
        if (fromAutosave)
        {
            var (dir, enabled) = HammerMcp.Core.Maps.Autosaves.HammerPlusPlusFolder(process.ExePath);
            if (dir is null)
                (dir, enabled) = StockAutosaveFolder();
            if (dir is not null && enabled && HammerMcp.Core.Maps.Autosaves.NewerThanMap(dir, map) is { } autosave)
                return (autosave.Path, $"autosave {System.IO.Path.GetFileName(autosave.Path)} ({autosave.Written:HH:mm:ss}, newer than the saved map)");
            return (map, dir is null ? "saved map (autosave folder unknown: Hammer++ settings / registry 'Autosave Dir' empty)" : "saved map (no newer autosave)");
        }
        return (map, "saved map");
    }

    /// <summary>Stock Hammer: HKCU\Software\Valve\Hammer\General "Autosave Dir" / "Autosaves Enabled".</summary>
    private static (string? Dir, bool Enabled) StockAutosaveFolder()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Valve\Hammer\General";
        var dir = Microsoft.Win32.Registry.GetValue(key, "Autosave Dir", null) as string;
        var enabled = Microsoft.Win32.Registry.GetValue(key, "Autosaves Enabled", 1) is not 0;
        return (string.IsNullOrWhiteSpace(dir) ? null : dir, enabled);
    }

    /// <summary>Shell channel bound to the target's shell window, resolved on every command.</summary>
    public ShellChannel Channel => new(() => Current()?.ShellWindow ?? 0);
}
