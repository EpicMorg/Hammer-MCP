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

    [McpServerTool(Name = "get_live_brush_faces", ReadOnly = true)]
    [Description("Read face planes, materials and VMF uaxis/vaxis (direction, shift and scale for each axis) of one live brush. Find a neighboring brush with list_live_brushes and choose a touching coplanar face with the same material as a UV reference. This tool does not apply textures.")]
    public LiveBrushFaces GetLiveBrushFaces(int brushId)
    {
        lock (_gate)
        {
            try
            {
                var brush = Snapshot(Open()).FindUnique(brushId);
                return brush is { Hidden: false }
                    ? new(true, "Live VMF face mapping", brush.Faces)
                    : new(false, "Brush ID is absent, hidden or ambiguous.", []);
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return new(false, ex.Message, []);
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
                Editor? editor = null;
                VmfBrushSnapshot? before = null, pasted = null;
                try
                {
                    editor = Open();
                    if (editor.Window.ProcessId != expected.Window.ProcessId || editor.Window.Document != expected.Window.Document ||
                        !string.Equals(editor.Path, expected.Path, StringComparison.OrdinalIgnoreCase))
                        return Refuse("The destination document changed while waiting for approval.");
                    before = Snapshot(editor);
                    var liveSource = Inspect(editor, copy: true);
                    if (liveSource.Brushes.Count != count || !Preserved(section, liveSource))
                        return Refuse("The source section changed while waiting for approval.");
                    CheckTarget(editor);
                    undo = 1;
                    editor.Window.PasteSelection();
                    pasted = Snapshot(editor);
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
                    if (undo == 0 || editor is null || before is null)
                        return Refuse(ex.Message);
                    try
                    {
                        CheckTarget(editor);
                        var now = Snapshot(editor);
                        if (SameSnapshot(before, now))
                            return Refuse($"{ex.Message} The original brushes are unchanged.");
                        if (undo == 2 && pasted is not null && !SameSnapshot(pasted, now))
                        {
                            if (!Preserved(before, now) || !SameBrushIds(pasted, now) ||
                                !editor.Window.UndoSectionAction(transformation: true))
                                throw new InvalidOperationException("Cannot identify the transformation in Undo history.");
                            now = Snapshot(editor);
                            if (!SameSnapshot(pasted, now))
                                throw new InvalidOperationException("Undo did not restore the pasted section.");
                        }
                        if (!Preserved(before, now) || now.Brushes.Count != before.Brushes.Count + count ||
                            !editor.Window.UndoSectionAction(transformation: false) ||
                            !SameSnapshot(before, Snapshot(editor)))
                            throw new InvalidOperationException("Could not confirm that Undo restored the original brushes.");
                        return Refuse($"{ex.Message} Rolled back the section and verified the original brushes.");
                    }
                    catch (Exception rollback) when (IsEditorError(rollback))
                    {
                        return Refuse($"{ex.Message} Rollback not verified: {rollback.Message} Inspect Hammer before retrying.");
                    }
                }
            }
        }
    }

    private static bool SameSnapshot(VmfBrushSnapshot a, VmfBrushSnapshot b) =>
        a.Brushes.Count == b.Brushes.Count && Preserved(a, b);

    private static bool SameBrushIds(VmfBrushSnapshot a, VmfBrushSnapshot b) =>
        a.Brushes.Count == b.Brushes.Count && a.Brushes.Select(x => x.Id).ToHashSet().SetEquals(b.Brushes.Select(x => x.Id));

    [McpServerTool(Name = "check_brush_clearance", ReadOnly = true)]
    [Description("Check one straight, level, axis-aligned passage for brush obstacles in the active Hammer++ document and optionally a section open in another document. Coordinates are player center XY and feet Z. Results are conservative: props and displacements are not collision-tested.")]
    public BrushClearanceResult CheckBrushClearance(float startX, float startY, float endX, float endY, float feetZ,
        string? sourcePath = null, float sectionMinX = 0, float sectionMinY = 0, float sectionMinZ = 0)
    {
        if (!(float.IsFinite(startX) && float.IsFinite(startY) && float.IsFinite(endX) &&
            float.IsFinite(endY) && float.IsFinite(feetZ) && float.IsFinite(sectionMinX) &&
            float.IsFinite(sectionMinY) && float.IsFinite(sectionMinZ)) ||
            (startX == endX) == (startY == endY))
            return new("error", [], "Supply a finite, nonzero, straight axis-aligned segment.");
        lock (_gate)
        {
            try
            {
                var editor = Open();
                var hits = new List<BrushClearanceHit>();
                var unknown = false;
                var min = new Vector3(MathF.Min(startX, endX) - 16, MathF.Min(startY, endY) - 16, feetZ);
                var max = new Vector3(MathF.Max(startX, endX) + 16, MathF.Max(startY, endY) + 16, feetZ + 72);
                Scan(Snapshot(editor), "destination", Vector3.Zero);
                if (sourcePath is not null)
                {
                    if (!Path.IsPathFullyQualified(sourcePath))
                        return new("error", [], "Supply the absolute path of the open source VMF.");
                    var process = target.Current() ?? throw new InvalidOperationException("Hammer closed.");
                    var section = editor.Window.InspectSection(process, sourcePath, copy: false);
                    if (section.Brushes.Count == 0)
                        return new("error", [], "The source has no brushes.");
                    Scan(section, "source", new Vector3(sectionMinX, sectionMinY, sectionMinZ) - Minimum(section.Brushes));
                }
                return new(hits.Count > 0 ? "brush_candidates" : unknown ? "unknown" : "no_brush_obstacle",
                    hits, unknown ? "Some geometry could not be verified (point entities, displacements or invalid brush bounds). This does not certify a walkable route."
                        : "Conservative brush bounds only; does not test floor support or in-game movement.");

                void Scan(VmfBrushSnapshot map, string document, Vector3 offset)
                {
                    unknown |= map.EntityCount != 0;
                    foreach (var brush in map.Brushes)
                    {
                        if (brush.Faces.Any(f => f.HasDisplacement))
                            unknown = true;
                        if (VmfBrushBounds.FromFaces(brush.Faces) is not { } bounds)
                        {
                            unknown = true;
                            continue;
                        }
                        var low = new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Min.Z) + offset;
                        var high = new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Max.Z) + offset;
                        if (low.X < max.X && high.X > min.X && low.Y < max.Y && high.Y > min.Y &&
                            low.Z < max.Z && high.Z > min.Z)
                            hits.Add(new(document, brush.Id, brush.EntityId, brush.Hidden,
                                new(low.X, low.Y, low.Z), new(high.X, high.Y, high.Z)));
                    }
                }
            }
            catch (Exception ex) when (IsEditorError(ex))
            {
                return new("error", [], ex.Message);
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
        double ax = (double)a.X - b.X, ay = (double)a.Y - b.Y, az = (double)a.Z - b.Z;
        double cx = (double)c.X - b.X, cy = (double)c.Y - b.Y, cz = (double)c.Z - b.Z;
        double nx = ay * cz - az * cy, ny = az * cx - ax * cz, nz = ax * cy - ay * cx;
        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (!(length > 0) || !double.IsFinite(length))
            throw new InvalidDataException("Hammer exported a degenerate brush plane.");
        nx /= length;
        ny /= length;
        nz /= length;
        var distance = nx * ((double)a.X - anchor.X) + ny * ((double)a.Y - anchor.Y) + nz * ((double)a.Z - anchor.Z);
        // Hammer can choose different points on the same plane after Paste.
        return face.Material.ToUpperInvariant() + FormattableString.Invariant(
            $":{Rounded(nx, 5):0.#####},{Rounded(ny, 5):0.#####},{Rounded(nz, 5):0.#####},{Rounded(distance, 3):0.###}");
    }

    private static double Rounded(double value, int digits)
    {
        var rounded = Math.Round(value, digits);
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

    [McpServerTool(Name = "preview_generated_section", ReadOnly = true)]
    [Description("Inspect a generated section before saving or inserting it. Reports total brushes, counts by owner class and material, and repeated stepped strips on straight runs that may be replaceable by a sloped brush. Does not modify the VMF.")]
    public object PreviewGeneratedSection(string vmf)
    {
        try
        {
            using var reader = new StringReader(vmf);
            var snapshot = VmfBrushSnapshot.Parse(reader);
            var strips = new List<(int Axis, float Width, float Depth, float Cross, float Bottom, float Start, float Top, string Material, int Id)>();
            foreach (var brush in snapshot.Brushes)
            {
                if (brush.Hidden || brush.EntityId is not null || brush.Faces.Any(f => f.HasDisplacement) ||
                    VmfBrushBounds.FromFaces(brush.Faces) is not { } bounds)
                    continue;
                var (min, max) = bounds;
                float dx = max.X - min.X, dy = max.Y - min.Y;
                if (dx > 32 || dy > 32 || max.Z - min.Z < 32 ||
                    brush.Faces.Select(f => f.Plane).Any(p => !IsAxial(p)))
                    continue;
                var material = string.Join("|", brush.Faces.Select(f => f.Material.ToUpperInvariant()).Order(StringComparer.Ordinal));
                strips.Add((0, dx, dy, min.Y, min.Z, min.X, max.Z, material, brush.Id));
                strips.Add((1, dy, dx, min.X, min.Z, min.Y, max.Z, material, brush.Id));
            }
            var runs = new List<int[]>();
            foreach (var group in strips.GroupBy(s => (s.Axis, s.Width, s.Depth, s.Cross, s.Bottom, s.Material)))
            {
                var ordered = group.OrderBy(s => s.Start).ToArray();
                for (int i = 0; i + 3 < ordered.Length;)
                {
                    int j = i + 1;
                    float rise = ordered[j].Top - ordered[i].Top;
                    while (rise != 0 && j < ordered.Length &&
                           MathF.Abs(ordered[j].Start - ordered[j - 1].Start - group.Key.Width) < 0.01f &&
                           MathF.Abs(ordered[j].Top - ordered[j - 1].Top - rise) < 0.01f)
                        j++;
                    if (j - i >= 4)
                    {
                        runs.Add(ordered[i..j].Select(s => s.Id).ToArray());
                        i = j;
                    }
                    else i++;
                }
            }
            return new { ok = true, brushCount = snapshot.Brushes.Count,
                byClass = snapshot.Brushes.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count()),
                byMaterial = snapshot.Brushes.SelectMany(b => b.Faces.Select(f => f.Material)
                    .Distinct(StringComparer.OrdinalIgnoreCase)).GroupBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count()),
                steppedStripCandidates = runs };
        }
        catch (Exception ex) when (IsEditorError(ex))
        {
            return new { ok = false, message = ex.Message };
        }
    }

    private static bool IsAxial(string plane)
    {
        var points = plane.Split(['(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (points.Length != 3 || VmfEntities.ParseVector(points[0]) is not { } a ||
            VmfEntities.ParseVector(points[1]) is not { } b || VmfEntities.ParseVector(points[2]) is not { } c)
            return false;
        return a.X == b.X && a.X == c.X || a.Y == b.Y && a.Y == c.Y || a.Z == b.Z && a.Z == c.Z;
    }

    [McpServerTool(Name = "save_generated_section", Destructive = false)]
    [Description("Save VMF text as a new .vmf in generated/ beside the active saved map. Pass a filename only, not a path. Never overwrites a file. Open the returned path in Hammer++ before insertion.")]
    public GeneratedSectionResult SaveGeneratedSection(string fileName, string vmf)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!fileName.EndsWith(".vmf", StringComparison.OrdinalIgnoreCase) || stem.Length is < 1 or > 100 ||
            !stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') || fileName.Length != stem.Length + 4)
            return new(false, null, "Use a simple .vmf filename with letters, digits, underscores or hyphens.");
        try
        {
            var process = target.Current();
            if (target.Control.Paused || process?.OpenMapPath is not { } map || !Path.IsPathFullyQualified(map) ||
                !map.EndsWith(".vmf", StringComparison.OrdinalIgnoreCase) || !File.Exists(map))
                return new(false, null, "Attach to an unpaused editor with a saved VMF open.");
            using var reader = new StringReader(vmf);
            if (VmfBrushSnapshot.Parse(reader).Brushes.Count == 0)
                return new(false, null, "The section must contain at least one brush.");
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(map)!, "generated"));
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return new(false, null, "The generated folder must not be a redirect or symlink.");
            string path = Path.Combine(directory.FullName, fileName);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(vmf);
            return new(true, path, "Saved generated section.");
        }
        catch (Exception ex) when (IsEditorError(ex))
        {
            return new(false, null, ex.Message);
        }
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

public sealed record BrushClearanceHit(string Document, int BrushId, int? EntityId, bool Hidden, BrushPoint Min, BrushPoint Max);
public sealed record BrushClearanceResult(string Status, IReadOnlyList<BrushClearanceHit> Candidates, string Message);

public sealed record LiveBrushFaces(bool Ok, string Message, IReadOnlyList<VmfBrushFace> Faces);

public sealed record GeneratedSectionResult(bool Ok, string? Path, string Message);
