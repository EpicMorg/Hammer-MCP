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

    [McpServerTool(Name = "insert_brush_section", Destructive = false)]
    [Description("Insert a brush-only VMF open in another document of the same Hammer++ instance. Position it by its minimum X/Y/Z plane-point bounds. Replaces the clipboard when explicitly allowed.")]
    public BrushEditResult InsertBrushSection(string sourcePath, float minX, float minY, float minZ,
        bool allowClipboardOverwrite = false)
    {
        var requested = new Vector3(minX, minY, minZ);
        if (!allowClipboardOverwrite || !Path.IsPathFullyQualified(sourcePath) ||
            !sourcePath.EndsWith(".vmf", StringComparison.OrdinalIgnoreCase) ||
            !float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(minZ))
            return Refuse("Specify an absolute section VMF path, finite minimum bounds, and allowClipboardOverwrite.");
        Editor expected;
        VmfBrushSnapshot section;
        lock (_gate)
        {
            try
            {
                expected = Open();
                section = Inspect(expected, copy: false);
                if (section.EntityCount != 0 || section.Brushes.Count == 0 ||
                    section.Brushes.Any(b => b.Hidden || b.Faces.Any(f => f.HasDisplacement)))
                    return Refuse("The section must contain visible world brushes only, without displacements.");
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return Refuse(ex.Message);
            }
        }

        int count = section.Brushes.Count;
        var preview = FormattableString.Invariant($"insert {count} brushes from {Path.GetFileName(sourcePath)} at minimum bounds ({minX}, {minY}, {minZ}); replaces the clipboard");
        return target.Changes.Run(preview, ApplySection, r => (r.Ok, r.Message), Refuse);

        VmfBrushSnapshot Inspect(Editor editor, bool copy) => editor.Window.InspectSection(
            target.Current() ?? throw new InvalidOperationException("Hammer closed."), sourcePath, copy);

        BrushEditResult ApplySection()
        {
            lock (_gate)
            {
                int undo = 0;
                try
                {
                    var editor = Open();
                    if (editor.Window.ProcessId != expected.Window.ProcessId || editor.Window.Document != expected.Window.Document ||
                        !string.Equals(editor.Path, expected.Path, StringComparison.OrdinalIgnoreCase))
                        return Refuse("The destination document changed while waiting for approval.");
                    var before = Snapshot(editor);
                    var liveSource = Inspect(editor, copy: true);
                    if (liveSource.Brushes.Count != count || !Preserved(section, liveSource))
                        return Refuse("The source section changed while waiting for approval.");
                    CheckTarget(editor);
                    undo = 1;
                    editor.Window.PasteSelection();
                    var pasted = Snapshot(editor);
                    var ids = before.Brushes.Select(b => b.Id).ToHashSet();
                    var added = pasted.Brushes.Where(b => !ids.Contains(b.Id)).ToArray();
                    if (added.Length != count || pasted.Brushes.Count != before.Brushes.Count + count ||
                        !Preserved(before, pasted) || !SameSection(section.Brushes, added))
                        throw new InvalidOperationException("Hammer did not paste the expected brush section.");
                    var anchor = Minimum(added);
                    var offset = requested - anchor;
                    if (offset != Vector3.Zero)
                    {
                        CheckTarget(editor);
                        undo = 2;
                        editor.Window.TransformSelection(false, offset);
                        var moved = Snapshot(editor);
                        var byId = moved.Brushes.ToLookup(b => b.Id);
                        if (!Preserved(before, moved) || moved.Brushes.Count != pasted.Brushes.Count ||
                            !added.All(b => byId[b.Id].Count() == 1 && Shape(byId[b.Id].First(), requested) == Shape(b, anchor)))
                            throw new InvalidOperationException("Hammer did not move only the pasted brushes to the requested coordinates.");
                    }
                    return new(true, $"Inserted {count} brushes. Undo: {undo} step(s).");
                }
                catch (Exception ex) when (IsEditorError(ex))
                {
                    return Refuse(undo == 0 ? ex.Message : $"{ex.Message} Inspect Hammer's Undo history ({undo} command(s) attempted).");
                }
            }
        }
    }

    private void CheckTarget(Editor editor)
    {
        if (target.Control.Paused || target.AttachedPid != editor.Window.ProcessId)
            throw new InvalidOperationException("The agent was paused or the editor changed.");
    }

    private static bool Preserved(VmfBrushSnapshot original, VmfBrushSnapshot current)
    {
        var byId = current.Brushes.ToLookup(b => b.Id);
        return original.EntityCount == current.EntityCount && original.Brushes.All(b =>
            byId[b.Id].Count() == 1 && byId[b.Id].First() is { } now &&
            b.Hidden == now.Hidden && b.EntityId == now.EntityId && b.SameGeometry(now));
    }

    private static Vector3 Minimum(IReadOnlyList<VmfBrush> brushes)
    {
        var result = new Vector3(float.PositiveInfinity);
        foreach (var b in brushes)
            result = Vector3.Min(result, new(b.PlanePointMin.X, b.PlanePointMin.Y, b.PlanePointMin.Z));
        return result;
    }

    private static bool SameSection(IReadOnlyList<VmfBrush> a, IReadOnlyList<VmfBrush> b)
    {
        var am = Minimum(a);
        var bm = Minimum(b);
        return a.Select(x => Shape(x, am)).Order(StringComparer.Ordinal)
            .SequenceEqual(b.Select(x => Shape(x, bm)).Order(StringComparer.Ordinal));
    }

    private static string Shape(VmfBrush brush, Vector3 anchor) => string.Join("|",
        brush.Faces.Select(face => PlaneKey(face, anchor)).Order(StringComparer.Ordinal));

    private static string PlaneKey(VmfBrushFace face, Vector3 anchor)
    {
        var points = face.Plane.Split(['(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (points.Length != 3 || VmfEntities.ParseVector(points[0]) is not { } a ||
            VmfEntities.ParseVector(points[1]) is not { } b || VmfEntities.ParseVector(points[2]) is not { } c)
            throw new InvalidDataException("Hammer exported an invalid brush plane.");
        var normal = Vector3.Normalize(Vector3.Cross(a - b, c - b));
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
            throw new InvalidDataException("Hammer exported a degenerate brush plane.");
        // Hammer can choose different points on the same plane after Paste.
        return face.Material.ToUpperInvariant() + FormattableString.Invariant(
            $":{Rounded(normal.X, 5):0.#####},{Rounded(normal.Y, 5):0.#####},{Rounded(normal.Z, 5):0.#####},{Rounded(Vector3.Dot(normal, a - anchor), 3):0.###}");
    }

    private static float Rounded(float value, int digits)
    {
        var rounded = MathF.Round(value, digits);
        return rounded == 0 ? 0 : rounded;
    }

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
