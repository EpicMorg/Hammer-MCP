using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HammerMcp.Shell;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class ViewportTools(HammerTarget target)
{
    private static int _captureInProgress;

    [McpServerTool(Name = "viewport_capture", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Return PNG images of the active map's visible viewport panes in Valve Hammer or Hammer++. Shows the current camera and unsaved edits without changing the map, camera or render mode. Does not capture the desktop. Requires a non-minimized editor on Windows.")]
    public async Task<CallToolResult> CaptureAsync(
        [Description("3d (default), 2d (orthographic panes), or all.")] string views = "3d",
        [Description("Maximum width or height in pixels (128–2048). Preserves aspect ratio without upscaling.")] int maxSize = 1280,
        CancellationToken cancellationToken = default)
    {
        var view = views?.ToLowerInvariant() switch
        {
            "3d" => HammerViewportKind.Camera,
            "2d" => HammerViewportKind.Orthographic,
            "all" => HammerViewportKind.All,
            _ => (HammerViewportKind?)null,
        };
        if (view is null)
            return Error("views must be 3d, 2d or all.");
        if (maxSize is < 128 or > 2048)
            return Error("maxSize must be between 128 and 2048.");
        if (!OperatingSystem.IsWindows())
            return Error("Viewport capture requires Windows.");
        var editor = target.Current();
        if (editor is null)
            return Error($"No Hammer is attached; {target.AttachHint}.");

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
            return Error("A viewport capture is already waiting for Hammer. Try again when the editor responds.");

        // PrintWindow has no cancellation or timeout. Keep at most one worker even if the request times out.
        var capture = Task.Run(() =>
        {
            try
            {
                var shot = HammerViewportCapture.Capture(editor, view.Value, maxSize);
                if (target.AttachedPid != editor.ProcessId)
                    return Error("The attached Hammer process changed during capture.");
                var content = new List<ContentBlock>(shot.Images.Count * 2 + shot.Warnings.Count);
                foreach (var warning in shot.Warnings)
                    content.Add(new TextContentBlock { Text = warning });
                foreach (var image in shot.Images)
                {
                    content.Add(new TextContentBlock { Text = $"{image.View} viewport ({image.Width} × {image.Height})" });
                    content.Add(ImageContentBlock.FromBytes(image.Png, "image/png"));
                }
                return new CallToolResult { Content = content, IsError = shot.Images.Count == 0 };
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                return Error(ex.Message);
            }
            finally
            {
                Volatile.Write(ref _captureInProgress, 0);
            }
        });
        try
        {
            return await capture.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (TimeoutException)
        {
            return Error("Hammer did not finish the capture in time. Close any modal dialogs and make sure Hammer is responding.");
        }
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };
}
