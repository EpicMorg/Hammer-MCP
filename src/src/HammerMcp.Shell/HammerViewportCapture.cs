using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HammerMcp.Shell;

public enum HammerViewportKind { Camera, Orthographic, All }

public sealed record HammerViewportImage(string View, int Width, int Height, ArraySegment<byte> Png);

public sealed record HammerViewportShot(IReadOnlyList<HammerViewportImage> Images, IReadOnlyList<string> Warnings);

[SupportedOSPlatform("windows")]
public static partial class HammerViewportCapture
{
    private const uint PrintClientAndRenderedContent = 0x0001 | 0x0002;

    public static HammerViewportShot Capture(HammerProcess editor, HammerViewportKind views, int maxSize)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            throw new InvalidOperationException("Viewport capture requires Windows 10 version 1607 or newer.");
        var frame = editor.MainWindow;
        if (editor.IsSource2 || frame == 0 ||
            !Path.GetFileName(editor.ExePath).Equals(editor.Name + ".exe", StringComparison.OrdinalIgnoreCase) ||
            !(editor.Name.Equals("hammer", StringComparison.OrdinalIgnoreCase) ||
              editor.Name.Equals("hammerplusplus", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Viewport capture requires standalone Source 1 Valve Hammer or Hammer++.");
        if (IsIconic(frame))
            throw new InvalidOperationException("Restore Hammer from the taskbar before capturing a viewport.");

        var document = HammerBrushWindow.ActiveDocument(frame);
        if (document == 0 || IsIconic(document) || !NativeMethods.IsWindowVisible(document))
            throw new InvalidOperationException("Open a visible map document in Hammer before capturing a viewport.");

        var panes = new List<(nint Window, bool Camera)>(4);
        EnumChildWindows(document, (wnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(wnd))
                return true;
            if (ViewKind(wnd) is { } kind && (views == HammerViewportKind.All || views == kind))
                panes.Add((wnd, kind == HammerViewportKind.Camera));
            return true;
        }, 0);
        if (panes.Count == 0)
            throw new InvalidOperationException("No requested viewport is visible in the open map. Restore its panes or choose another view.");

        var images = new List<HammerViewportImage>(panes.Count);
        var warnings = new List<string>();
        var orthographic = 0;
        foreach (var pane in panes)
        {
            if (HammerBrushWindow.ActiveDocument(frame) != document || !NativeMethods.IsWindowVisible(pane.Window))
                throw new InvalidOperationException("The active Hammer document or viewport changed during capture.");
            var label = pane.Camera ? "3D" : $"2D {++orthographic}";
            if (CapturePane(pane.Window, label, pane.Camera, maxSize) is { } image)
                images.Add(image);
            else
                warnings.Add("The 3D viewport did not render a usable frame. Bring Hammer to the foreground and aim the camera at geometry, then try again.");
        }
        if (HammerBrushWindow.ActiveDocument(frame) != document || IsIconic(frame))
            throw new InvalidOperationException("The active Hammer document or viewport changed during capture.");
        return new(images, warnings);
    }

    private static unsafe HammerViewportKind? ViewKind(nint window)
    {
        Span<char> buffer = stackalloc char[128];
        int length;
        fixed (char* ptr = buffer)
            length = NativeMethods.GetClassName(window, ptr, buffer.Length);
        ReadOnlySpan<char> name = buffer[..length];
        // MFC encodes RegisterClass styles in Afx:<module>:<style>:...; the pane classes are 0x102b
        // (camera) and 0x1008 (orthographic) in both editors. Their view labels are separate child HWNDs.
        if (!name.StartsWith("Afx:", StringComparison.OrdinalIgnoreCase) ||
            NativeMethods.FindWindowEx(window, 0, null, "Title Window") == 0)
            return null;
        var moduleEnd = name[4..].IndexOf(':');
        if (moduleEnd < 0)
            return null;
        var style = name[(moduleEnd + 5)..];
        if (style.StartsWith("102b:", StringComparison.OrdinalIgnoreCase)) return HammerViewportKind.Camera;
        if (style.StartsWith("1008:", StringComparison.OrdinalIgnoreCase)) return HammerViewportKind.Orthographic;
        return null;
    }

    [SupportedOSPlatform("windows10.0.14393")]
    private static HammerViewportImage? CapturePane(nint window, string label, bool camera, int maxSize)
    {
        // Match the editor window's DPI awareness: legacy Hammer prints at logical size even on a scaled monitor.
        var previous = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
        if (previous == 0)
            throw new InvalidOperationException("Could not read the viewport's DPI context.");
        try
        {
            if (!GetClientRect(window, out var rect) || rect.Right <= 0 || rect.Bottom <= 0 ||
                (long)rect.Right * rect.Bottom > 34_000_000)
                throw new InvalidOperationException($"The {label} viewport has no usable capture area.");

            using var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var dc = graphics.GetHdc();
                try
                {
                    if (!PrintWindow(window, dc, PrintClientAndRenderedContent))
                        throw new InvalidOperationException($"Windows could not capture the {label} viewport. Restore Hammer and try again.");
                }
                finally
                {
                    graphics.ReleaseHdc(dc);
                }
            }

            if (camera && HasFlatScene(bitmap))
                return null;

            if (Math.Max(bitmap.Width, bitmap.Height) <= maxSize)
                return Encode(bitmap, label);
            var ratio = (double)maxSize / Math.Max(bitmap.Width, bitmap.Height);
            using var scaled = new Bitmap(Math.Max(1, (int)Math.Round(bitmap.Width * ratio)),
                Math.Max(1, (int)Math.Round(bitmap.Height * ratio)), PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(bitmap, 0, 0, scaled.Width, scaled.Height);
            }
            return Encode(scaled, label);
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    private static unsafe bool HasFlatScene(Bitmap bitmap)
    {
        // Ignore the Camera label and pane borders. Legacy Hammer can return only that chrome
        // when the render surface is unavailable. Check all interior pixels to retain thin wireframes.
        var rect = new Rectangle(2, 32, bitmap.Width - 4, bitmap.Height - 34);
        if (rect.Width <= 0 || rect.Height <= 0)
            return true;
        var pixels = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            const uint rgb = 0x00ff_ffff;
            var first = ((uint*)pixels.Scan0)[0] & rgb;
            for (int y = 0; y < rect.Height; y++)
            {
                var row = (uint*)(pixels.Scan0 + y * pixels.Stride);
                for (int x = 0; x < rect.Width; x++)
                    if ((row[x] & rgb) != first)
                        return false;
            }
            return true;
        }
        finally
        {
            bitmap.UnlockBits(pixels);
        }
    }

    private static HammerViewportImage Encode(Bitmap bitmap, string label)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return new(label, bitmap.Width, bitmap.Height, new(png.GetBuffer(), 0, (int)png.Length));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(nint parent, NativeMethods.EnumWindowsProc callback, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowDpiAwarenessContext(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);
}
