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

    /// <summary>Shell channel bound to the target's shell window, resolved on every command.</summary>
    public ShellChannel Channel => new(() => Current()?.ShellWindow ?? 0);
}
