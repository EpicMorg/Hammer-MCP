using System.Globalization;
using System.Numerics;

namespace HammerMcp.Shell;

/// <summary>Formats shell commands exactly like Source SDK editor_sendcommand.cpp.</summary>
public static class ShellCommands
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string SessionBegin(string mapName, int mapVersion) => $"session_begin {mapName} {mapVersion}";

    public static string SessionEnd() => "session_end";

    public static string MapCheckVersion(string mapName, int mapVersion) => $"map_check_version {mapName} {mapVersion}";

    public static string EntityCreate(string className, Vector3 origin) =>
        string.Create(Inv, $"entity_create {className} {origin.X:G} {origin.Y:G} {origin.Z:G}");

    public static string EntityDelete(string className, Vector3 origin) =>
        string.Create(Inv, $"entity_delete {className} {origin.X:G} {origin.Y:G} {origin.Z:G}");

    public static string EntitySetKeyValue(string className, Vector3 origin, string key, string value) =>
        string.Create(Inv, $"entity_set_keyvalue {className} {origin.X:F6} {origin.Y:F6} {origin.Z:F6} \"{Quote(key)}\" \"{Quote(value)}\"");

    public static string EntityRotateIncremental(string className, Vector3 origin, Vector3 pitchYawRoll) =>
        string.Create(Inv, $"entity_rotate_incremental {className} {origin.X:F6} {origin.Y:F6} {origin.Z:F6} {pitchYawRoll.X:F6} {pitchYawRoll.Y:F6} {pitchYawRoll.Z:F6}");

    public static string NodeCreate(string nodeClass, int id, Vector3 origin) =>
        string.Create(Inv, $"node_create {nodeClass} {id} {origin.X:G} {origin.Y:G} {origin.Z:G}");

    public static string NodeDelete(int id) => string.Create(Inv, $"node_delete {id}");

    public static string NodeLinkCreate(int startId, int endId) => string.Create(Inv, $"nodelink_create {startId} {endId}");

    public static string NodeLinkDelete(int startId, int endId) => string.Create(Inv, $"nodelink_delete {startId} {endId}");

    // Hammer's parser has no escaping; a double quote inside a value would end the token.
    private static string Quote(string s) =>
        s.Contains('"') ? throw new ArgumentException("shell channel values cannot contain '\"'", nameof(s)) : s;
}
