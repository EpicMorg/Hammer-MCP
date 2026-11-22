using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HammerShell.Client;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    public const uint WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public nuint dwData;
        public uint cbData;
        public nint lpData;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowA", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, ref COPYDATASTRUCT lParam);
}
