using System.ComponentModel;
using System.Numerics;
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
public sealed class EntityTools(HammerTarget target, ActivityLog log, EditJournal journal)
{
    [McpServerTool(Name = "create_entity", Destructive = false)]
    [Description("Create a point entity in the map open in Hammer, optionally setting keyvalues. Refused if an entity of the same class already sits at this origin (it could not be addressed afterwards) - pick other coordinates. Brush entities (trigger_*, func_*) cannot be created this way.")]
    public EditResult CreateEntity(
        [Description("Entity class, e.g. info_target, light, prop_dynamic")] string className,
        float x, float y, float z,
        [Description("Keyvalues to set after creation, e.g. {\"targetname\": \"door_light\"}")] Dictionary<string, string>? keyValues = null)
    {
        var origin = new Vector3(x, y, z);
        return Edit(className, origin, expectExisting: false, (session, map) =>
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
        return Edit(className, origin, expectExisting: true,
            (session, _) => [Run(session, ShellCommands.EntitySetKeyValue(className, origin, key, value))]);
    }

    [McpServerTool(Name = "rotate_entity")]
    [Description("Rotate the entity of this class at this origin by pitch/yaw/roll degrees (incremental). Refused if several entities of this class share the origin. Stock Hammer applies it only to entities with a model (prop_*); for others (info_target, ...) set the angles keyvalue with set_keyvalue.")]
    public EditResult RotateEntity(string className, float x, float y, float z, float pitch, float yaw, float roll)
    {
        var origin = new Vector3(x, y, z);
        return Edit(className, origin, expectExisting: true,
            (session, _) => [Run(session, ShellCommands.EntityRotateIncremental(className, origin, new Vector3(pitch, yaw, roll)))]);
    }

    [McpServerTool(Name = "delete_entity", Destructive = true)]
    [Description("Delete the entity of this class at this origin. Refused if several entities of this class share the origin (it is not possible to choose which one).")]
    public EditResult DeleteEntity(string className, float x, float y, float z)
    {
        var origin = new Vector3(x, y, z);
        return Edit(className, origin, expectExisting: true, (session, map) =>
        {
            var line = Run(session, ShellCommands.EntityDelete(className, origin));
            if (line.EndsWith("= Ok", StringComparison.Ordinal))
                journal.Deleted(map, className, origin);
            return [line];
        });
    }

    private EditResult Edit(string className, Vector3 origin, bool expectExisting, Func<ShellSession, string, List<string>> action)
    {
        var process = target.Current();
        if (process is null || !process.HasShellChannel)
            return new EditResult(false, "no Hammer with a shell channel is attached - ask the user to attach a Hammer process in Hammer-MCP", []);
        var mapPath = process.OpenMapPath;
        if (mapPath is null)
            return new EditResult(false, "no map is open in Hammer", []);
        if (!File.Exists(mapPath))
            return new EditResult(false, $"map file not found: {mapPath} (save the map first)", []);

        var saved = VmfEntities.Read(mapPath);
        var count = journal.Count(mapPath, saved, className, origin);
        var at = $"{className} at {origin.X:G} {origin.Y:G} {origin.Z:G}";
        if (!expectExisting && count > 0)
            return Refuse($"{count} {at} already exist(s); a second one could not be addressed - use other coordinates");
        if (expectExisting && count > 1)
        {
            var ids = string.Join(", ", saved.At(className, origin).Select(e => $"id {e.Id}{(e.TargetName is null ? "" : $" '{e.TargetName}'")}"));
            return Refuse($"{count} entities {at} share this address ({ids}); the shell channel cannot tell them apart - "
                          + "ask the user to move or delete the extra ones in Hammer");
        }

        using var session = ShellSession.Begin(target.Channel, mapPath);
        if (session is null)
            return new EditResult(false, "Hammer refused the session: save the map (Ctrl+S) and retry", []);
        var commands = action(session, mapPath);
        foreach (var line in commands)
            log.Write($"shell: {line}");
        var ok = commands.All(l => l.EndsWith("= Ok", StringComparison.Ordinal));
        var message = ok
            ? $"done (session version {session.AcceptedVersion})"
            : expectExisting && count == 0
                ? $"refused by Hammer: no {at} in the saved map nor in this session's edits"
                : "some commands were refused by Hammer";
        return new EditResult(ok, message, commands);
    }

    private EditResult Refuse(string reason)
    {
        log.Write($"refused: {reason}");
        return new EditResult(false, reason, []);
    }

    private static string Run(ShellSession session, string command) => $"{command} = {session.Send(command)}";
}

public sealed record EditResult(bool Ok, string Message, IReadOnlyList<string> Commands);
