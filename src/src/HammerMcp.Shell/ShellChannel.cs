using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace HammerMcp.Shell;

public enum ShellResult
{
    Ok,
    /// <summary>Hammer received the command and refused it (0 from WM_COPYDATA).</summary>
    Rejected,
    /// <summary>No Worldcraft_ShellMessageWnd window: no Hammer with a shell channel is running.</summary>
    NotRunning,
}

/// <summary>
/// Hammer shell channel: hidden window "Worldcraft_ShellMessageWnd", text commands via WM_COPYDATA
/// (same as src/public/editor_sendcommand.cpp in the Source SDK). Write-only: the answer is accepted / refused.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellChannel
{
    public const string WindowName = "Worldcraft_ShellMessageWnd";

    private readonly Func<nint> _window;

    /// <summary>Talks to the first Hammer with a shell window.</summary>
    public ShellChannel() : this(FindWindow)
    {
    }

    /// <summary>Talks to a specific shell window (e.g. of the attached Hammer process).</summary>
    public ShellChannel(Func<nint> window) => _window = window;

    // Commands are ANSI in Hammer; the system code page keeps non-ASCII keyvalues as Hammer expects them.
    private static readonly Encoding CommandEncoding = CodePagesEncoding();

    public static nint FindWindow() => NativeMethods.FindWindow(WindowName, WindowName);

    public ShellResult Send(string command)
    {
        var hwnd = _window();
        if (hwnd == 0)
            return ShellResult.NotRunning;

        var bytes = CommandEncoding.GetBytes(command + '\0');
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var cds = new NativeMethods.COPYDATASTRUCT
            {
                dwData = 0,
                cbData = (uint)bytes.Length,
                lpData = handle.AddrOfPinnedObject(),
            };
            return NativeMethods.SendMessage(hwnd, NativeMethods.WM_COPYDATA, 0, ref cds) != 0
                ? ShellResult.Ok
                : ShellResult.Rejected;
        }
        finally
        {
            handle.Free();
        }
    }

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(0);
    }
}
