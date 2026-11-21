using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;

namespace HammerMcp.Shell;

/// <summary>Reads Hammer's standard Win32 status bar through MSAA, without reading editor memory.</summary>
[SupportedOSPlatform("windows")]
internal static class HammerSelectionStatus
{
    private const uint ObjIdClient = 0xffff_fffc;

    public static BrushSelection? Read(nint frame)
    {
        var status = NativeMethods.GetDlgItem(frame, 59393); // MFC AFX_IDW_STATUS_BAR
        var iid = typeof(IAccessible).GUID;
        if (status == 0 || AccessibleObjectFromWindow(status, ObjIdClient, in iid, out var accessible) != 0 || accessible is null)
            return null;
        try
        {
            return BrushSelection.FromStatus(accessible[2] ?? "", accessible[4] ?? "");
        }
        finally
        {
            Marshal.ReleaseComObject(accessible);
        }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessible
    {
        [DispId(-5003), IndexerName("accName")]
        string? this[[MarshalAs(UnmanagedType.Struct)] object child] { [return: MarshalAs(UnmanagedType.BStr)] get; }
    }
}
