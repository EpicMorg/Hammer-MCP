using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Registry;
using Microsoft.Win32;

namespace HammerMcp.Shell;

/// <summary>Stock Source 1 Hammer UI. Every command is addressed to the attached editor, not keyboard focus.</summary>
[SupportedOSPlatform("windows")]
public sealed class HammerBrushWindow
{
    private const int Export = 32900, Transform = 113, Delete = 32858;
    private const int Accept = 1, Cancel = 2, MoveRadio = 1194, ScaleRadio = 1259;
    private const uint WmCommand = 0x0111, WmSetText = 0x000c, WmMdiGetActive = 0x0229;
    private const uint BmClick = 0x00f5, BmGetCheck = 0x00f0;
    private const uint GwOwner = 4, GwChild = 5, GwNext = 2;
    private const uint AbortIfHung = 0x0002, ErrorOnExit = 0x0020;
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(4);
    private const string LastGoodSave = "Last Good Save";
    private readonly string _recoveryKey;

    public int ProcessId { get; }
    public nint Frame { get; }
    public nint Document { get; }

    public HammerBrushWindow(HammerProcess process)
    {
        if (!process.TryGetStandaloneSource1(out var hammerPlusPlus))
            throw new InvalidOperationException("Live brush editing requires standalone Source 1 Valve Hammer or Hammer++.");
        ProcessId = process.ProcessId;
        _recoveryKey = hammerPlusPlus
            ? @"Software\Valve\Hammer\HPP"
            : @"Software\Valve\Hammer\General";
        Frame = process.MainWindow;
        Document = ActiveDocument(Frame);
        if (Document == 0)
            throw new InvalidOperationException("Open a map in Hammer before using live brush tools.");
    }

    public static nint ActiveDocument(nint frame)
    {
        if (frame == 0)
            return 0;
        var mdi = NativeMethods.FindWindowEx(frame, 0, "MDIClient", null);
        return mdi != 0 && NativeMethods.SendMessageTimeout(mdi, WmMdiGetActive, 0, 0, AbortIfHung | ErrorOnExit,
            4000, out var document) != 0 ? document : 0;
    }

    public VmfBrushSnapshot Snapshot()
    {
        // The common dialog may keep its destination as the current directory. Delete only the file.
        var path = Path.Combine(Path.GetTempPath(), $"hammer-mcp-brush-{Guid.NewGuid():N}.vmf");
        using var recovery = Registry.CurrentUser.OpenSubKey(_recoveryKey, writable: true);
        var previousSave = recovery?.GetValue(LastGoodSave) as string;
        try
        {
            Dialog(Export, "Export As", dialog =>
            {
                // Modern common dialogs nest the filename edit (1001); the classic dialog uses edt1 (1152).
                var filename = FindEdit(dialog, 1001);
                if (filename == 0)
                    filename = FindEdit(dialog, 1152);
                if (filename == 0)
                    throw new InvalidOperationException("Hammer's VMF export dialog did not expose a filename field.");
                SetText(filename, path);
            });
            if (!File.Exists(path))
                throw new InvalidOperationException("Hammer did not export a VMF. Check that the export file type is Valve Map Files (*.vmf).");
            using var reader = File.OpenText(path);
            return VmfBrushSnapshot.Parse(reader);
        }
        finally
        {
            // Export changes Hammer's crash-recovery entry. Never leave it pointing to a file we're deleting.
            if (recovery?.GetValue(LastGoodSave) is string current && current.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                if (previousSave is null)
                    recovery.DeleteValue(LastGoodSave, throwOnMissingValue: false);
                else
                    recovery.SetValue(LastGoodSave, previousSave, RegistryValueKind.String);
            }
            File.Delete(path);
        }
    }

    public BrushSelection? ReadSelection()
    {
        EnsureReady();
        return HammerSelectionStatus.Read(Frame);
    }

    public void Select(int brushId) => Dialog(MenuCommands.GoToBrushNumber, "Go to Brush ID", dialog =>
        SetText(Edit(dialog, 1009), brushId.ToString(CultureInfo.InvariantCulture)));

    public void TransformSelection(bool scale, Vector3 values) => Dialog(Transform, "Transformation", dialog =>
    {
        var radio = Control(dialog, scale ? ScaleRadio : MoveRadio, "Button");
        Send(radio, BmClick);
        if (Send(radio, BmGetCheck) != 1)
            throw new InvalidOperationException("Hammer did not activate the requested transformation mode.");
        SetText(Edit(dialog, 114), values.X.ToString("G9", CultureInfo.InvariantCulture));
        SetText(Edit(dialog, 115), values.Y.ToString("G9", CultureInfo.InvariantCulture));
        SetText(Edit(dialog, 116), values.Z.ToString("G9", CultureInfo.InvariantCulture));
    });

    public void DeleteSelection(BrushSelection expected)
    {
        if (ReadSelection() != expected)
            throw new InvalidOperationException("The Hammer selection changed before Delete; no edit was applied.");
        Send(Frame, WmCommand, Delete);
        EnsureReady();
    }

    private void Dialog(int command, string title, Action<nint> fill)
    {
        EnsureReady();
        // Keep the original command in flight while we fill its modal dialog. Its return is the
        // completion signal: an export may close the Save dialog and write or reopen the file afterwards.
        var dispatch = Task.Run(() => NativeMethods.SendMessageTimeout(Frame, WmCommand, command, 0,
            AbortIfHung | ErrorOnExit, 15000, out _) != 0);

        nint dialog = 0;
        WaitUntil(() => (dialog = OwnedDialog()) != 0 || dispatch.IsCompleted);
        if (dialog == 0)
            throw new InvalidOperationException($"Hammer did not open {title}. Check that the requested operation is available.");
        if (!WindowText(dialog).Equals(title, StringComparison.OrdinalIgnoreCase) ||
            !ClassIs(dialog, "#32770"))
            throw new InvalidOperationException($"Expected {title}, but Hammer opened '{WindowText(dialog)}'. No edit was confirmed.");

        try
        {
            fill(dialog);
            Control(dialog, Accept, "Button");
            if (!NativeMethods.PostMessage(dialog, WmCommand, Accept, 0) || !dispatch.GetAwaiter().GetResult())
                throw new InvalidOperationException($"Hammer did not finish {title}. Check for an open dialog.");
            EnsureReady();
        }
        finally
        {
            // Never dismiss a different dialog, including an error or confirmation from the editor.
            if (NativeMethods.IsWindowVisible(dialog) && NativeMethods.GetWindow(dialog, GwOwner) == Frame &&
                NativeMethods.IsWindowEnabled(dialog) && WindowText(dialog).Equals(title, StringComparison.OrdinalIgnoreCase))
                NativeMethods.PostMessage(dialog, WmCommand, Cancel, 0);
        }
    }

    private void EnsureReady()
    {
        NativeMethods.GetWindowThreadProcessId(Frame, out var pid);
        if (pid != ProcessId || ActiveDocument(Frame) != Document)
            throw new InvalidOperationException("The active Hammer document changed. Refresh live brushes and retry.");
        if (!NativeMethods.IsWindowEnabled(Frame) || OwnedDialog() != 0)
            throw new InvalidOperationException("Close Hammer's open dialog before using live brush tools.");
    }

    private nint OwnedDialog()
    {
        nint result = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.GetWindow(hwnd, GwOwner) != Frame || !NativeMethods.IsWindowVisible(hwnd) || !ClassIs(hwnd, "#32770"))
                return true;
            result = hwnd;
            return false;
        }, 0);
        return result;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        long before = Stopwatch.GetTimestamp();
        do
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        } while (Stopwatch.GetElapsedTime(before) < DialogTimeout);
        return false;
    }

    private static nint FindEdit(nint window, int id)
    {
        var edit = NativeMethods.GetDlgItem(window, id);
        if (edit != 0 && ClassIs(edit, "Edit"))
            return edit;
        for (var child = NativeMethods.GetWindow(window, GwChild); child != 0; child = NativeMethods.GetWindow(child, GwNext))
        {
            if (FindEdit(child, id) is var found && found != 0)
                return found;
        }
        return 0;
    }

    private static nint Edit(nint dialog, int id) => Control(dialog, id, "Edit");

    private static nint Control(nint dialog, int id, string type)
    {
        var control = NativeMethods.GetDlgItem(dialog, id);
        if (control == 0 || !ClassIs(control, type) || !NativeMethods.IsWindowEnabled(control))
            throw new InvalidOperationException($"Hammer's dialog does not have the expected {type} control ({id}).");
        return control;
    }

    private static nint Send(nint window, uint message, nint wParam = 0, nint lParam = 0)
    {
        if (NativeMethods.SendMessageTimeout(window, message, wParam, lParam, AbortIfHung | ErrorOnExit, 4000, out var result) == 0)
            throw new InvalidOperationException("Hammer did not respond to its window command.");
        return result;
    }

    private static unsafe void SetText(nint control, string value)
    {
        fixed (char* text = value)
            if (Send(control, WmSetText, 0, (nint)text) == 0)
                throw new InvalidOperationException("Hammer refused the dialog input.");
    }

    private static unsafe bool ClassIs(nint window, string type)
    {
        char* chars = stackalloc char[64];
        int count = NativeMethods.GetClassName(window, chars, 64);
        return new ReadOnlySpan<char>(chars, count).Equals(type, StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe string WindowText(nint window)
    {
        char* chars = stackalloc char[256];
        int count = NativeMethods.GetWindowText(window, chars, 256);
        return new string(chars, 0, count);
    }
}
