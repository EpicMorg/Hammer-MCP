using System.ComponentModel;
using System.Numerics;
using System.Runtime.Versioning;
using HammerMcp.Core.Maps;
using HammerShell.Client;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>
/// Live edits through the shell channel. Entities are addressed by class + origin (no ids in the channel), so every
/// edit is checked first against the saved .vmf plus this server's edits since the last save: an address must point
/// to exactly one entity, and new entities must not share an address. One session per call.
/// </summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class EntityTools(HammerTarget target, ActivityLog log, EditJournal journal, HammerMcpServerOptions options)
{
    [McpServerTool(Name = "create_entity", Destructive = false)]
    [Description("Create a point entity in the map open in Hammer, optionally setting keyvalues. Refused if an entity of the same class already sits at this origin (it could not be addressed afterwards) - pick other coordinates. Brush entities (trigger_*, func_*) cannot be created this way.")]
    public EditResult CreateEntity(
        [Description("Entity class, e.g. info_target, light, prop_dynamic")] string className,
        float x, float y, float z,
        [Description("Keyvalues to set after creation, e.g. {\"targetname\": \"door_light\"}")] Dictionary<string, string>? keyValues = null)
    {
        var origin = new Vector3(x, y, z);
        var kv = keyValues is { Count: > 0 } ? ", " + string.Join(", ", keyValues.Select(p => $"{p.Key} = \"{p.Value}\"")) : "";
        return Edit($"create {className} at {Xyz(origin)}{kv}", className, origin, expectExisting: false, (session, map) =>
        {
            var results = new List<string> { Run(session, ShellCommands.EntityCreate(className, origin)) };
            if (results[0].EndsWith("= Ok", StringComparison.Ordinal))
                journal.Created(map, className, origin);
            foreach (var (key, value) in keyValues ?? [])
                results.Add(Run(session, ShellCommands.EntitySetKeyValue(className, origin, key, value)));
            return results;
        });
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
    private EditResult Edit(string what, string className, Vector3 origin, bool expectExisting, Func<ShellSession, string, List<string>> action)
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
            () => Apply(mapPath, className, origin, expectExisting, action),
            r => (r.Ok, r.Commands.Count == 0 ? r.Message : $"{r.Message}: {string.Join("; ", r.Commands)}"),
            reason => Refuse(reason));
    }

    /// <summary>
    /// Checks the address and runs the edit. With approval this runs when the user applies it, maybe minutes later: the
    /// map may have changed meanwhile (another map opened, the entity moved, a second one placed at the address).
    /// </summary>
    private EditResult Apply(string mapPath, string className, Vector3 origin, bool expectExisting, Func<ShellSession, string, List<string>> action)
    {
        if (target.Control.Paused)
            return Refuse(AgentControl.PausedReason);
        var now = target.Current()?.OpenMapPath;
        if (!string.Equals(now, mapPath, StringComparison.OrdinalIgnoreCase))
            return Refuse($"another map is open in Hammer now ({now ?? "none"}) - not applied");
        if (!File.Exists(mapPath))
            return Refuse($"map file not found: {mapPath} (save the map first)");
        if (CheckAddress(mapPath, className, origin, expectExisting, out var count) is { } refusal)
            return Refuse(refusal);

        using var session = ShellSession.Begin(target.Channel, Path.GetFileNameWithoutExtension(mapPath), VmfVersionInfo.Read(mapPath).MapVersion);
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
