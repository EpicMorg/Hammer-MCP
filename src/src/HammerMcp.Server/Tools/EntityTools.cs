using System.ComponentModel;
using System.Numerics;
using System.Globalization;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;
using HammerMcp.Shell;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>
/// Live edits through the shell channel. Entities are addressed by class + origin (no ids in the channel), so every
/// edit is checked first against the saved .vmf plus this server's edits since the last save: an address must point
/// to exactly one entity, and new entities must not share an address. One session per call.
/// </summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class EntityTools(HammerTarget target, ActivityLog log, EditJournal journal, HammerMcpServerOptions options, GameData gameData)
{
    [McpServerTool(Name = "place_prop_on_surface", Destructive = false)]
    [Description("Place a Source 1 prop_static by live VMF brush ID. Supports rectangular world brushes and their top displacements with vertical offsets. The rotated footprint must fit entirely on the surface. On uneven ground the highest point under the footprint sets the height; the tool does not align the prop to the slope.")]
    public EditResult PlacePropOnSurface(int supportBrushId, string model, float x, float y,
        float pitch = 0, float yaw = 0, float roll = 0, string? configName = null)
    {
        if (target.Control.Paused || options.AdapterOnlyRefusal is not null)
            return Refuse(options.AdapterOnlyRefusal ?? AgentControl.PausedReason);
        var process = target.Current();
        if (process is not { HasShellChannel: true, OpenMapPath: { } path })
            return Refuse("Attach to Source 1 Hammer with a saved map and a shell channel.");
        model = model.Replace('\\', '/');
        if (!model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ||
            !model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ||
            model.Split('/').Any(part => part is "" or "." or "..") ||
            model.IndexOfAny([':', '"', '\r', '\n', '\0']) >= 0)
            return Refuse("Supply a relative models/...mdl path.");
        try
        {
            var config = gameData.Select(configName);
            if (config.Active is not { } active || gameData.Assets(active) is not { } assets)
                return Refuse(config.Problem ?? "No Source 1 asset search paths are available.");
            if (assets.Read(model) is not { } mdl || assets.Read(model[..^4] + ".vvd") is not { } vvd)
                return Refuse("The model or its VVD was not found in the game search paths.");
            var window = new HammerBrushWindow(process);
            VmfBrush Support(HammerBrushWindow editor)
            {
                var snapshot = editor.Snapshot();
                target.NoteBrushSnapshotVersion(editor, path, snapshot.MapVersion);
                return snapshot.FindUnique(supportBrushId) ?? throw new InvalidDataException("Support brush ID is absent or ambiguous.");
            }
            var support = Support(window);
            var angles = new Vector3(pitch, yaw, roll);
            var origin = PropPlacement.OnBrush(support, mdl.Data, vvd.Data, x, y, angles);
            return CreatePoint("prop_static", origin, new Dictionary<string, string> { ["model"] = model, ["angles"] = Xyz(angles) },
                () =>
                {
                    var current = target.Current();
                    if (current?.ProcessId != process.ProcessId || !string.Equals(current.OpenMapPath, path, StringComparison.OrdinalIgnoreCase))
                        return "The active Hammer document changed.";
                    try
                    {
                        var editor = new HammerBrushWindow(current);
                        if (editor.Document != window.Document)
                            return "The active Hammer document changed.";
                        var live = Support(editor);
                        return live.Hidden || live.EntityId is not null || !live.SameGeometry(support)
                            ? "The support brush changed; recalculate placement." : null;
                    }
                    catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        return e.Message;
                    }
                });
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Refuse(e.Message);
        }
    }

    [McpServerTool(Name = "place_prop_on_prop", Destructive = false)]
    [Description("Place a Source 1 prop_static on a visible live prop ID (list_entities supplies saved IDs). XY is the new model's origin. surfaceZ selects the nearest horizontal layer within 8 Hammer units; omit it for the top layer under the footprint center. Uses render triangles with rotation and scale. Refuses incomplete support or intersection with the support, conservatively using the placed model's footprint and envelope. Does not verify in-game physics.")]
    public EditResult PlacePropOnProp(int supportEntityId, string model, float x, float y, float? surfaceZ = null,
        float pitch = 0, float yaw = 0, float roll = 0, string? configName = null)
    {
        if (target.Control.Paused || options.AdapterOnlyRefusal is not null)
            return Refuse(options.AdapterOnlyRefusal ?? AgentControl.PausedReason);
        var process = target.Current();
        if (process is not { HasShellChannel: true, OpenMapPath: { } path })
            return Refuse("Attach to Source 1 Hammer with a saved map and a shell channel.");
        try
        {
            var config = gameData.Select(configName);
            if (config.Active is not { } active || gameData.Assets(active) is not { } assets)
                return Refuse(config.Problem ?? "No Source 1 asset search paths are available.");
            var window = new HammerBrushWindow(process);
            var support = FindSupport(window, out _);
            model = ModelPath(model);
            var angles = new Vector3(pitch, yaw, roll);
            var placed = Mesh(model, StudioMesh.Transform(default, angles, 1), 0, true);
            var surface = Mesh(ModelPath(support.Model), StudioMesh.Transform(support.Position, support.Angles, support.Scale),
                support.Body, false);
            var origin = PropContact.Place(surface, placed, new(x, y), surfaceZ);
            return CreatePoint("prop_static", origin,
                new Dictionary<string, string> { ["model"] = model, ["angles"] = Xyz(angles) }, Guard);

            string? Guard()
            {
                try
                {
                    var current = target.Current();
                    if (current?.ProcessId != process.ProcessId ||
                        !string.Equals(current.OpenMapPath, path, StringComparison.OrdinalIgnoreCase))
                        return "The active Hammer document changed.";
                    var editor = new HammerBrushWindow(current);
                    if (editor.Document != window.Document)
                        return "The active Hammer document changed.";
                    if (FindSupport(editor, out var entities) != support)
                        return "The supporting prop changed; recalculate placement.";
                    if (entities.At("prop_static", origin).Any())
                        return "A prop_static already occupies the computed origin.";
                    return null;
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return e.Message;
                }
            }

            PropSupport FindSupport(HammerBrushWindow editor, out List<VmfEntity> entities)
            {
                var snapshot = editor.SnapshotEntities();
                entities = snapshot.Entities;
                target.NoteBrushSnapshotVersion(editor, path, snapshot.MapVersion);
                VmfEntity? prop = null;
                foreach (var entity in entities)
                {
                    if (entity.Id != supportEntityId)
                        continue;
                    if (prop is not null)
                        throw new InvalidDataException("Support entity ID is ambiguous.");
                    prop = entity;
                }
                return PropSupport.Read(prop);
            }

            Vector3[] Mesh(string file, Matrix4x4 transform, int body, bool requireStatic)
            {
                if (assets.Read(file) is not { } mdl || assets.Read(file[..^4] + ".vvd") is not { } vvd ||
                    assets.Read(file[..^4] + ".dx90.vtx") is not { } vtx)
                    throw new InvalidDataException($"Missing MDL, VVD or DX90 VTX for {file} in the active game paths.");
                return StudioMesh.Read(mdl.Data, vvd.Data, vtx.Data, transform, body, requireStatic);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Refuse(e.Message);
        }
    }

    private static string ModelPath(string model)
    {
        model = model.Replace('\\', '/');
        if (!model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ||
            !model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ||
            model.Split('/').Any(part => part is "" or "." or "..") ||
            model.IndexOfAny([':', '"', '\r', '\n', '\0']) >= 0)
            throw new InvalidDataException("Supply a relative models/...mdl path.");
        return model;
    }

    private sealed record PropSupport(string Model, Vector3 Position, Vector3 Angles, float Scale, int Body)
    {
        public static PropSupport Read(VmfEntity? prop)
        {
            if (prop is not { Hidden: false, HasBrushes: false, Origin: { } position } ||
                !prop.ClassName.StartsWith("prop_", StringComparison.OrdinalIgnoreCase) ||
                prop.KeyValues.GetValueOrDefault("model") is not { } model)
                throw new InvalidDataException("Support ID must identify a visible live point prop with a model.");
            var kv = prop.KeyValues;
            if (!string.IsNullOrWhiteSpace(kv.GetValueOrDefault("parentname")) ||
                !string.IsNullOrWhiteSpace(kv.GetValueOrDefault("DefaultAnim")))
                throw new InvalidDataException("The transform of a parented or animated prop cannot be verified.");
            var angles = VmfEntities.ParseVector(kv.GetValueOrDefault("angles") ?? "0 0 0");
            if (angles is null ||
                !float.TryParse(kv.GetValueOrDefault("modelscale") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
                !int.TryParse(kv.GetValueOrDefault("body") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var body))
                throw new InvalidDataException("The supporting prop has invalid angles, scale or body.");
            return new(model, position, angles.Value, scale, body);
        }
    }

    [McpServerTool(Name = "create_entity", Destructive = false)]
    [Description("Create a point entity in the map open in Hammer, optionally setting keyvalues. Refused if an entity of the same class already sits at this origin (it could not be addressed afterwards) - pick other coordinates. Brush entities (trigger_*, func_*) cannot be created this way.")]
    public EditResult CreateEntity(
        [Description("Entity class, e.g. info_target, light, prop_dynamic")] string className,
        float x, float y, float z,
        [Description("Keyvalues to set after creation, e.g. {\"targetname\": \"door_light\"}")] Dictionary<string, string>? keyValues = null) =>
        CreatePoint(className, new Vector3(x, y, z), keyValues);

    private EditResult CreatePoint(string className, Vector3 origin, Dictionary<string, string>? keyValues, Func<string?>? guard = null)
    {
        var kv = keyValues is { Count: > 0 } ? ", " + string.Join(", ", keyValues.Select(p => $"{p.Key} = \"{p.Value}\"")) : "";
        return Edit($"create {className} at {Xyz(origin)}{kv}", className, origin, expectExisting: false, (session, map) =>
        {
            var results = new List<string> { Run(session, ShellCommands.EntityCreate(className, origin)) };
            if (results[0].EndsWith("= Ok", StringComparison.Ordinal))
                journal.Created(map, className, origin);
            foreach (var (key, value) in keyValues ?? [])
                results.Add(Run(session, ShellCommands.EntitySetKeyValue(className, origin, key, value)));
            return results;
        }, guard);
    }

    [McpServerTool(Name = "set_keyvalue")]
    [Description("Set one keyvalue on the entity of this class at this origin. Refused if several entities of this class share the origin. Not recorded in Hammer's undo history (Ctrl+Z will not revert it): note the old value (get_entity) if you may need to restore it. Also sets angles directly, e.g. key angles = \"0 90 0\".")]
    public EditResult SetKeyValue(string className, float x, float y, float z, string key, string value)
    {
        var origin = new Vector3(x, y, z);
        return Edit($"set {key} = \"{value}\" on {className} at {Xyz(origin)}", className, origin, expectExisting: true,
            (session, _) => [Run(session, ShellCommands.EntitySetKeyValue(className, origin, key, value))]);
    }

    [McpServerTool(Name = "rotate_entity")]
    [Description("Rotate the entity of this class at this origin by pitch/yaw/roll degrees (incremental). Refused if several entities of this class share the origin. Stock Hammer applies it only to entities with a model (prop_*); for others (info_target, ...) set the angles keyvalue with set_keyvalue.")]
    public EditResult RotateEntity(string className, float x, float y, float z, float pitch, float yaw, float roll)
    {
        var origin = new Vector3(x, y, z);
        return Edit($"rotate {className} at {Xyz(origin)} by {Xyz(new Vector3(pitch, yaw, roll))} (pitch yaw roll)", className, origin, expectExisting: true,
            (session, _) => [Run(session, ShellCommands.EntityRotateIncremental(className, origin, new Vector3(pitch, yaw, roll)))]);
    }

    [McpServerTool(Name = "delete_entity", Destructive = true)]
    [Description("Delete the entity of this class at this origin. Refused if several entities of this class share the origin (it is not possible to choose which one). A brush entity (trigger_*, func_*) is deleted together with its brushes; undo brings it back.")]
    public EditResult DeleteEntity(string className, float x, float y, float z)
    {
        var origin = new Vector3(x, y, z);
        return Edit($"delete {className} at {Xyz(origin)}", className, origin, expectExisting: true, (session, map) =>
        {
            var line = Run(session, ShellCommands.EntityDelete(className, origin));
            if (line.EndsWith("= Ok", StringComparison.Ordinal))
                journal.Deleted(map, className, origin);
            return [line];
        });
    }

    /// <summary>
    /// Runs the edit right away, or (approval mode) once the user applies <paramref name="what"/> in the app or in
    /// Hammer; the address is checked against the saved map at that moment.
    /// </summary>
    private EditResult Edit(string what, string className, Vector3 origin, bool expectExisting, Func<ShellSession, string, List<string>> action,
        Func<string?>? guard = null)
    {
        if (target.Control.Paused)
            return Refuse(AgentControl.PausedReason);
        if (options.AdapterOnlyRefusal is { } adapterOnly)
            return Refuse(adapterOnly);
        var process = target.Current();
        if (process is null || !process.HasShellChannel)
            return new EditResult(false, $"no Hammer with a shell channel is attached - {target.AttachHint}", []);
        var mapPath = process.OpenMapPath;
        if (mapPath is null)
            return new EditResult(false, "no map is open in Hammer", []);
        if (!File.Exists(mapPath))
            return new EditResult(false, $"map file not found: {mapPath} (save the map first)", []);

        // approval mode: refuse a bad address before it reaches the user's list
        if (target.Changes.AskUser && CheckAddress(mapPath, className, origin, expectExisting, out _) is { } refusal)
            return Refuse(refusal);

        return target.Changes.Run(what,
            () => Apply(mapPath, className, origin, expectExisting, action, guard),
            r => (r.Ok, r.Commands.Count == 0 ? r.Message : $"{r.Message}: {string.Join("; ", r.Commands)}"),
            reason => Refuse(reason));
    }

    /// <summary>
    /// Checks the address and runs the edit. With approval this runs when the user applies it, maybe minutes later: the
    /// map may have changed meanwhile (another map opened, the entity moved, a second one placed at the address).
    /// </summary>
    private EditResult Apply(string mapPath, string className, Vector3 origin, bool expectExisting,
        Func<ShellSession, string, List<string>> action, Func<string?>? guard)
    {
        if (target.Control.Paused)
            return Refuse(AgentControl.PausedReason);
        var process = target.Current();
        var now = process?.OpenMapPath;
        if (process is null || !string.Equals(now, mapPath, StringComparison.OrdinalIgnoreCase))
            return Refuse($"another map is open in Hammer now ({now ?? "none"}) - not applied");
        if (!File.Exists(mapPath))
            return Refuse($"map file not found: {mapPath} (save the map first)");
        if (CheckAddress(mapPath, className, origin, expectExisting, out var count) is { } refusal)
            return Refuse(refusal);
        if (guard?.Invoke() is { } blocked)
            return Refuse(blocked);

        using var session = ShellSession.Begin(target.Channel, mapPath, () => target.BrushSnapshotVersion(process, mapPath));
        if (session is null)
            return new EditResult(false, "Hammer refused the session: save the map (Ctrl+S) and retry", []);
        var commands = action(session, mapPath);
        foreach (var line in commands)
            log.Write($"shell: {line}");
        var ok = commands.All(l => l.EndsWith("= Ok", StringComparison.Ordinal));
        var message = ok
            ? $"done (session version {session.AcceptedVersion})"
            : expectExisting && count == 0
                ? $"refused by Hammer: no {At(className, origin)} in the saved map nor in this session's edits"
                : "some commands were refused by Hammer";
        return new EditResult(ok, message, commands);
    }

    /// <summary>Null = the address is usable: exactly one entity there (edit) or none (create).</summary>
    private string? CheckAddress(string mapPath, string className, Vector3 origin, bool expectExisting, out int count)
    {
        var saved = VmfEntities.Read(mapPath);
        count = journal.Count(mapPath, saved, className, origin);
        var at = At(className, origin);
        if (!expectExisting && count > 0)
            return $"{count} {at} already exist(s); a second one could not be addressed - use other coordinates";
        if (expectExisting && count > 1)
        {
            var ids = string.Join(", ", saved.At(className, origin).Select(e => $"id {e.Id}{(e.TargetName is null ? "" : $" '{e.TargetName}'")}"));
            return $"{count} entities {at} share this address ({ids}); the shell channel cannot tell them apart - "
                   + "ask the user to move or delete the extra ones in Hammer";
        }
        return null;
    }

    private EditResult Refuse(string reason)
    {
        log.Write($"refused: {reason}");
        return new EditResult(false, reason, []);
    }

    private static string Xyz(Vector3 v) => FormattableString.Invariant($"{v.X:G} {v.Y:G} {v.Z:G}");

    private static string At(string className, Vector3 origin) => $"{className} at {Xyz(origin)}";

    private static string Run(ShellSession session, string command) => $"{command} = {session.Send(command)}";
}

public sealed record EditResult(bool Ok, string Message, IReadOnlyList<string> Commands);
