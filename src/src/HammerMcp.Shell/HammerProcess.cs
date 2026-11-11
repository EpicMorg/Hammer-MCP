using System.Runtime.Versioning;
using HammerMcp.Core.Maps;

namespace HammerMcp.Shell;

/// <summary>The running Hammer that owns the shell window: process, exe path and the open map from the main window title.</summary>
[SupportedOSPlatform("windows")]
public sealed record HammerProcess(int ProcessId, string ExePath, IReadOnlyList<string> WindowTitles)
{
    /// <summary>Map path from a title like "Hammer++ - 64 Bit - [B:\...\map.vmf - Textured Shaded]".</summary>
    public string? OpenMapPath =>
        WindowTitles.Select(HammerWindowTitle.ParseMapPath).FirstOrDefault(p => p is not null);

    public static HammerProcess? FindShellOwner()
    {
        var hwnd = ShellChannel.FindWindow();
        if (hwnd == 0)
            return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return new HammerProcess((int)pid, QueryExePath(pid) ?? "?", VisibleTitles(pid));
    }

    private static unsafe string? QueryExePath(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static unsafe List<string> VisibleTitles(uint pid)
    {
        var titles = new List<string>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !NativeMethods.IsWindowVisible(hwnd))
                return true;
            var length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
                return true;
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
            {
                var n = NativeMethods.GetWindowText(hwnd, p, buffer.Length);
                titles.Add(new string(buffer, 0, n));
            }
            return true;
        }, 0);
        return titles;
    }
}
