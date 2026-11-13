using System.Diagnostics;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;

namespace HammerMcp.Shell;

/// <summary>A running editor process: exe, its shell window (if any), main window and the open map from the title.</summary>
[SupportedOSPlatform("windows")]
public sealed record HammerProcess(int ProcessId, string Name, string ExePath, nint ShellWindow, nint MainWindow, IReadOnlyList<string> WindowTitles)
{
    /// <summary>Process names of known editors (without .exe), used besides shell-window owners.</summary>
    private static readonly HashSet<string> EditorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hammer", "hammerplusplus", "wc", "jack", "jackhammer", "Sledge.Editor",
    };

    /// <summary>Source 2 games whose Workshop Tools run Hammer inside the game process (game/bin/win64/tools/hammer.dll).</summary>
    private static readonly HashSet<string> Source2Games = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2", "dota2", "hlvr", "project8", "deadlock",
    };

    public bool HasShellChannel => ShellWindow != 0;

    /// <summary>Exe name is one of the known map editors (Hammer, Hammer++, Worldcraft, J.A.C.K., Sledge).</summary>
    public bool IsKnownEditorName => EditorNames.Contains(Name);

    /// <summary>Map path from a title like "Hammer++ - 64 Bit - [B:\...\map.vmf - Textured Shaded]".</summary>
    public string? OpenMapPath =>
        WindowTitles.Select(HammerWindowTitle.ParseMapPath).FirstOrDefault(p => p is not null);

    public string MainTitle => WindowTitles.FirstOrDefault(t => t.Contains('[')) ?? WindowTitles.FirstOrDefault() ?? Name;

    /// <summary>The first process that owns a shell window (what the Python smoke scripts use).</summary>
    public static HammerProcess? FindShellOwner() =>
        ShellOwners().Select(o => FromPid(o.Pid, o.Shell)).FirstOrDefault(p => p is not null);

    /// <summary>Editors: shell-window owners + known editor exe names; with <paramref name="allProcesses"/> every process with a window.</summary>
    public static List<HammerProcess> List(bool allProcesses)
    {
        var shells = ShellOwners().GroupBy(o => o.Pid).ToDictionary(g => g.Key, g => g.First().Shell);
        var result = new Dictionary<int, HammerProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var pid = process.Id;
                var interesting = shells.ContainsKey(pid)
                                  || EditorNames.Contains(process.ProcessName)
                                  || (Source2Games.Contains(process.ProcessName) && HasSource2Hammer(pid))
                                  || (allProcesses && SafeMainWindow(process) != 0);
                if (!interesting)
                    continue;
                if (FromPid(pid, shells.GetValueOrDefault(pid)) is { } info)
                    result[pid] = info;
            }
        }
        return [.. result.Values.OrderByDescending(p => p.HasShellChannel).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Re-reads windows and titles of a process; null if it exited.</summary>
    public static HammerProcess? Refresh(int pid)
    {
        var shell = ShellOwners().FirstOrDefault(o => o.Pid == pid).Shell;
        return FromPid(pid, shell);
    }

    private static HammerProcess? FromPid(int pid, nint shellWindow)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var windows = VisibleWindows((uint)pid);
            var main = windows.FirstOrDefault(w => w.Title.Contains('[')).Hwnd;
            if (main == 0)
                main = windows.FirstOrDefault().Hwnd;
            return new HammerProcess(pid, process.ProcessName, QueryExePath((uint)pid) ?? "?", shellWindow, main,
                [.. windows.Select(w => w.Title)]);
        }
        catch (ArgumentException)
        {
            return null; // exited
        }
    }

    private static bool HasSource2Hammer(int pid) =>
        QueryExePath((uint)pid) is { } exe && Path.GetDirectoryName(exe) is { } dir && File.Exists(Path.Combine(dir, "tools", "hammer.dll"));

    private static nint SafeMainWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static IEnumerable<(int Pid, nint Shell)> ShellOwners()
    {
        nint hwnd = 0;
        while ((hwnd = NativeMethods.FindWindowEx(0, hwnd, ShellChannel.WindowName, ShellChannel.WindowName)) != 0)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            yield return ((int)pid, hwnd);
        }
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

    private static unsafe List<(nint Hwnd, string Title)> VisibleWindows(uint pid)
    {
        var windows = new List<(nint, string)>();
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
                windows.Add((hwnd, new string(buffer, 0, n)));
            }
            return true;
        }, 0);
        return windows;
    }
}
