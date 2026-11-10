using System.ComponentModel;
using System.Numerics;
using HammerMcp.Shell;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>
/// Live edits through the shell channel. Entities are addressed by class + origin (no ids in the channel);
/// the map must be saved so the session version matches the .vmf. One session per call for now.
/// </summary>
[McpServerToolType]
public sealed class EntityTools(ShellChannel channel)
{
    [McpServerTool(Name = "create_entity", Destructive = false)]
    [Description("Create a point entity in the map open in Hammer, optionally setting keyvalues. Brush entities (trigger_*, func_*) cannot be created this way.")]
    public EditResult CreateEntity(
        [Description("Entity class, e.g. info_target, light, prop_dynamic")] string className,
        float x, float y, float z,
        [Description("Keyvalues to set after creation, e.g. {\"targetname\": \"door_light\"}")] Dictionary<string, string>? keyValues = null)
    {
        var origin = new Vector3(x, y, z);
        return Edit(session =>
        {
            var results = new List<string> { Run(session, ShellCommands.EntityCreate(className, origin)) };
            foreach (var (key, value) in keyValues ?? [])
                results.Add(Run(session, ShellCommands.EntitySetKeyValue(className, origin, key, value)));
            return results;
        });
    }

    [McpServerTool(Name = "set_keyvalue")]
    [Description("Set one keyvalue on the entity of this class at this origin.")]
    public EditResult SetKeyValue(string className, float x, float y, float z, string key, string value) =>
        Edit(session => [Run(session, ShellCommands.EntitySetKeyValue(className, new Vector3(x, y, z), key, value))]);

    [McpServerTool(Name = "rotate_entity")]
    [Description("Rotate the entity of this class at this origin by pitch/yaw/roll degrees (incremental).")]
    public EditResult RotateEntity(string className, float x, float y, float z, float pitch, float yaw, float roll) =>
        Edit(session => [Run(session, ShellCommands.EntityRotateIncremental(className, new Vector3(x, y, z), new Vector3(pitch, yaw, roll)))]);

    [McpServerTool(Name = "delete_entity", Destructive = true)]
    [Description("Delete the entity of this class at this origin. If several identical entities share the origin, one of them is deleted.")]
    public EditResult DeleteEntity(string className, float x, float y, float z) =>
        Edit(session => [Run(session, ShellCommands.EntityDelete(className, new Vector3(x, y, z)))]);

    private EditResult Edit(Func<ShellSession, List<string>> action)
    {
        var mapPath = HammerProcess.FindShellOwner()?.OpenMapPath;
        if (mapPath is null)
            return new EditResult(false, "Hammer with a shell channel is not running or no map is open", []);
        if (!File.Exists(mapPath))
            return new EditResult(false, $"map file not found: {mapPath} (save the map first)", []);

        using var session = ShellSession.Begin(channel, mapPath);
        if (session is null)
            return new EditResult(false, "Hammer refused the session: save the map (Ctrl+S) and retry", []);
        var log = action(session);
        var ok = log.All(l => l.EndsWith("= Ok", StringComparison.Ordinal));
        return new EditResult(ok, ok ? $"done (session version {session.AcceptedVersion})" : "some commands were refused", log);
    }

    private static string Run(ShellSession session, string command) => $"{command} = {session.Send(command)}";
}

public sealed record EditResult(bool Ok, string Message, IReadOnlyList<string> Commands);
