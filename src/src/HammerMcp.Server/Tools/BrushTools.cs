using System.ComponentModel;
using System.Numerics;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;
using HammerMcp.Shell;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class BrushTools(HammerTarget target, ActivityLog log)
{
    private readonly object _gate = new();

    [McpServerTool(Name = "list_live_brushes", ReadOnly = true)]
    [Description("List brush IDs, materials and plane-point bounds from the open Source 1 Hammer map, including unsaved edits. Use a unique visible ID with move_brush, scale_brush or delete_brush. Uses Hammer's VMF Export to a temporary file; it does not save or reload the open map.")]
    public LiveBrushList ListLiveBrushes(int offset = 0, int limit = 100,
        [Description("Case-insensitive material substring (optional).")] string? material = null)
    {
        if (offset < 0 || limit is < 1 or > 500)
            return new(false, "offset must be non-negative and limit must be between 1 and 500", 0, []);
        lock (_gate)
        {
            try
            {
                var editor = Open();
                var map = Snapshot(editor);
                var page = new List<LiveBrushInfo>(Math.Min(limit, map.Brushes.Count));
                int matches = 0;
                foreach (var brush in map.Brushes)
                {
                    if (!string.IsNullOrEmpty(material) &&
                        !brush.Faces.Any(face => face.Material.Contains(material, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (matches++ < offset || page.Count == limit)
                        continue;
                    page.Add(new(brush.Id, brush.EntityId, brush.ClassName, brush.Hidden, brush.PlanePointMin,
                        brush.PlanePointMax, brush.Faces.Select(face => face.Material)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
                }
                return new(true, "live VMF snapshot (unsaved changes included)", matches, page);
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return new(false, ex.Message, 0, []);
            }
        }
    }

    [McpServerTool(Name = "move_brush", Destructive = false)]
    [Description("Translate a brush by x/y/z Hammer units and record it in Hammer's Undo history. Omit brushId to use the single currently selected brush; otherwise use an ID from list_live_brushes.")]
    public BrushEditResult MoveBrush(float x, float y, float z, int? brushId = null) =>
        Edit(Operation.Move, new(x, y, z), brushId);

    [McpServerTool(Name = "scale_brush")]
    [Description("Scale a brush through Hammer's Transformation dialog. X/y/z are positive factors (1 leaves that axis unchanged). Omit brushId to use the single currently selected brush. Undo in Hammer restores the brush.")]
    public BrushEditResult ScaleBrush(float x, float y, float z, int? brushId = null) =>
        Edit(Operation.Scale, new(x, y, z), brushId);

    [McpServerTool(Name = "delete_brush", Destructive = true)]
    [Description("Delete one brush by its live VMF ID, or omit brushId to delete the single currently selected brush. Hammer records the edit in Undo. A brush entity may be removed if this is its last brush.")]
    public BrushEditResult DeleteBrush(int? brushId = null) => Edit(Operation.Delete, default, brushId);

    private BrushEditResult Edit(Operation operation, Vector3 values, int? id)
    {
        if (id is <= 0)
            return Refuse("brushId must be a positive ID from list_live_brushes");
        if (!float.IsFinite(values.X) || !float.IsFinite(values.Y) || !float.IsFinite(values.Z))
            return Refuse("all transform values must be finite");
        if (operation == Operation.Move && values == Vector3.Zero)
            return Refuse("move requires a nonzero displacement");
        if (operation == Operation.Scale && (values.X <= 0 || values.Y <= 0 || values.Z <= 0 || values == Vector3.One))
            return Refuse("scale factors must all be positive and at least one must differ from 1");
        if (!target.Changes.AskUser)
            return Apply(operation, values, id, null);

        Prepared prepared;
        lock (_gate)
        {
            try
            {
                var editor = Open();
                var before = Snapshot(editor);
                prepared = new(editor, Resolve(before, id, id is null ? editor.Window.ReadSelection() : null));
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return Refuse(ex.Message);
            }
        }
        var action = operation.ToString().ToLowerInvariant();
        var parameters = operation == Operation.Delete ? "" : FormattableString.Invariant($" ({values.X:G}, {values.Y:G}, {values.Z:G})");
        var source = id is null ? "currently selected " : "";
        return target.Changes.Run($"{action} {source}brush #{prepared.Brush.Id}{parameters}",
            () => Apply(operation, values, id, prepared), result => (result.Ok, result.Message), Refuse);
    }

    private BrushEditResult Apply(Operation operation, Vector3 values, int? id, Prepared? prepared)
    {
        lock (_gate)
        {
            var sentEdit = false;
            try
            {
                var editor = Open();
                if (prepared is not null && (editor.Window.ProcessId != prepared.Editor.Window.ProcessId ||
                    editor.Window.Document != prepared.Editor.Window.Document ||
                    !string.Equals(editor.Path, prepared.Editor.Path, StringComparison.OrdinalIgnoreCase)))
                    return Refuse("the active Hammer document changed while waiting for approval");

                var before = Snapshot(editor);
                var originalSelection = id is null ? editor.Window.ReadSelection() : null;
                var brush = Resolve(before, id, originalSelection);
                if (prepared is not null && (brush.Id != prepared.Brush.Id || !brush.SameGeometry(prepared.Brush)))
                    return Refuse("the brush or selection changed while waiting for approval; refresh and ask again");
                if (id is not null)
                    editor.Window.Select(brush.Id);
                var selection = editor.Window.ReadSelection();
                if (selection is null || (id is null ? selection != originalSelection : !selection.Matches(brush)))
                    return Refuse("Hammer did not select exactly the requested brush; no edit was applied");
                if (target.Control.Paused || target.AttachedPid != editor.Window.ProcessId)
                    return Refuse("the agent was paused or the attached editor changed; no edit was applied");

                sentEdit = true;
                if (operation == Operation.Delete)
                    editor.Window.DeleteSelection(selection);
                else
                    editor.Window.TransformSelection(operation == Operation.Scale, values);

                var after = Snapshot(editor);
                var verified = operation == Operation.Delete
                    ? after.Brushes.Count == before.Brushes.Count - 1 && !after.Brushes.Any(b => b.Id == brush.Id)
                    : after.FindUnique(brush.Id) is { } changed && !brush.SameGeometry(changed);
                if (!verified)
                    return Refuse($"Hammer did not confirm the expected geometry change for brush #{brush.Id}. Inspect the map and Undo history before retrying.");

                var message = $"brush #{brush.Id}: {operation.ToString().ToLowerInvariant()} applied in Hammer";
                log.Write($"brush: {message}");
                return new(true, message, brush.Id);
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return Refuse(sentEdit ? $"{ex.Message} Inspect the map and Undo history before retrying." : ex.Message);
            }
        }
    }

    private static VmfBrush Resolve(VmfBrushSnapshot map, int? id, BrushSelection? selection)
    {
        if (id is null)
            return selection?.Resolve(map)
                ?? throw new InvalidOperationException("Select exactly one identifiable brush, with Hammer's status bar visible, or supply a brushId from list_live_brushes.");
        return map.FindUnique(id.Value) is { Hidden: false } brush ? brush
            : throw new InvalidOperationException($"Brush #{id} is absent, hidden or ambiguous in the live map; refresh list_live_brushes.");
    }

    private Editor Open()
    {
        if (target.Control.Paused)
            throw new InvalidOperationException(AgentControl.PausedReason);
        var process = target.Current() ?? throw new InvalidOperationException($"No Hammer is attached; {target.AttachHint}.");
        return new(new HammerBrushWindow(process), process.OpenMapPath);
    }

    private VmfBrushSnapshot Snapshot(Editor editor)
    {
        var snapshot = editor.Window.Snapshot();
        target.NoteBrushSnapshotVersion(editor.Window, editor.Path, snapshot.MapVersion);
        return snapshot;
    }

    private BrushEditResult Refuse(string message)
    {
        log.Write($"brush: {message}");
        return new(false, message);
    }

    private static bool IsEditorError(Exception ex) => ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException;

    private enum Operation { Move, Scale, Delete }
    private sealed record Editor(HammerBrushWindow Window, string? Path);
    private sealed record Prepared(Editor Editor, VmfBrush Brush);
}

public sealed record LiveBrushList(bool Ok, string Message, int Total, IReadOnlyList<LiveBrushInfo> Brushes);
public sealed record LiveBrushInfo(int Id, int? EntityId, string ClassName, bool Hidden, BrushPoint PlanePointMin,
    BrushPoint PlanePointMax, IReadOnlyList<string> Materials);
public sealed record BrushEditResult(bool Ok, string Message, int? BrushId = null);
