using System.Runtime.Versioning;

namespace HammerMcp.Shell;

/// <summary>Posts main-menu commands (WM_COMMAND) to Hammer's main window. Works also without a shell channel (Strata).</summary>
[SupportedOSPlatform("windows")]
public static class MenuChannel
{
    private const uint WM_COMMAND = 0x0111;

    /// <summary>Posted, not sent: dialogs (Run Map, Check for Problems) open without blocking the caller.</summary>
    public static bool Post(nint mainWindow, int commandId) =>
        mainWindow != 0 && NativeMethods.PostMessage(mainWindow, WM_COMMAND, commandId, 0);
}
