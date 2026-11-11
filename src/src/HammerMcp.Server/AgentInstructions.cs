namespace HammerMcp.Server;

/// <summary>
/// Sent to every MCP client in the initialize response ("instructions"): how to work with Hammer through this server.
/// Keep it short and factual; tool-specific details belong in the tool descriptions.
/// </summary>
internal static class AgentInstructions
{
    public const string Text = """
        You are connected to Valve Hammer Editor (Source engine map editor) running on the user's PC.
        Start with hammer_status: it tells which Hammer is attached, whether live edits are allowed, which map is open
        and its saved mapversion. Use map_summary to see what the saved map contains.

        How live edits work (shell channel):
        - Edits go into the map currently open in Hammer and appear immediately; the user can undo them with Ctrl+Z.
        - The map must be saved first: sessions are bound to the mapversion of the saved .vmf. If an edit is refused,
          call save_map and retry.
        - Entities are addressed by class + origin (x y z), there are no ids. Two entities of the same class at the
          same origin cannot be told apart: the server refuses such edits and refuses to create a second entity at an
          occupied address. Use list_entities to find exact origins; place new entities at distinct coordinates.
        - Only point entities can be created (light, info_target, prop_dynamic, ...). Brush entities (trigger_*,
          func_*) need brushes and cannot be created; you can still set keyvalues on existing ones.
        - There is no move command; outputs (I/O connections) cannot be set through this channel yet.
        - Reading reflects the SAVED .vmf only. After your edits, call save_map before reading the map again.

        Units: Hammer units (1 unit = 0.75 inch for player scale, a player is 72 units tall, a standard door 112).
        Coordinates are world x y z; angles are pitch yaw roll in degrees.
        Be careful with the user's map: prefer small, reversible steps and say what you changed.
        """;
}
