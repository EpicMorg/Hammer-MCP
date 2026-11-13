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
        and its saved mapversion. Use map_summary / list_entities / get_entity / find_io to read the saved map, and
        fgd_class / fgd_search for the game's entity reference (keyvalues, choices, inputs, outputs) before creating or
        changing entities. validate_map finds broken outputs and unknown classes.

        How live edits work (shell channel):
        - Edits go into the map currently open in Hammer and appear immediately. Creation, deletion and rotation can be
          undone (undo / Ctrl+Z); keyvalue changes cannot - read the old value with get_entity first if it matters.
        - The map must be saved first: sessions are bound to the mapversion of the saved .vmf. If an edit is refused,
          call save_map and retry.
        - Entities are addressed by class + origin (x y z), there are no ids. Two entities of the same class at the
          same origin cannot be told apart: the server refuses such edits and refuses to create a second entity at an
          occupied address. Use list_entities to find exact origins; place new entities at distinct coordinates.
        - Only point entities can be created (light, info_target, prop_dynamic, ...). Brush entities (trigger_*,
          func_*) need brushes and cannot be created; you can still edit existing ones: address them by their origin,
          which is the centre of their brushes (list_entities shows it as origin, or as center if the .vmf has none).
        - There is no move command.
        - Outputs (I/O): set_keyvalue with the output name as key and "target,Input,parameter,delay,times" as value
          (e.g. OnTrigger = "door1,Open,,0,-1"; times -1 = always). Hammer keeps it as a plain keyvalue, so it is not
          shown on the Outputs tab, but vbsp compiles it into a real output and it fires in game; tell the user.
          Never put double quotes inside a value: Hammer and the compilers cut the value there.
        - Reading reflects the SAVED .vmf. With Hammer++, read tools accept fromAutosave=true to use its newest autosave
          when that is newer (a few minutes old at most). After your edits, call save_map before reading the map again.

        Compiling (edit modes only): compile_sequences shows Hammer's own Run Map sequences; run_compile runs one on the
        SAVED map (ask the user first), compile_log / leak_trace explain the result, bsp_entities shows what ended up
        in the compiled .bsp.

        Source 2 Hammer (CS2, Dota 2, HL: Alyx; runs inside the game process): read-only. Map tools read the saved
        .vmap (entities, keyvalues, outputs, validation); there is no shell channel and no menu commands, so ask the user
        to make changes in Hammer and save (Ctrl+S) before you read again. source2_console talks to the game's developer
        console: hammer_map_files lists the open maps. Read tools with fromAutosave=true force an autosave and read it, so
        you see the user's unsaved edits without asking them to save.

        Units: Hammer units (1 unit = 0.75 inch for player scale, a player is 72 units tall, a standard door 112).
        Coordinates are world x y z; angles are pitch yaw roll in degrees.
        Be careful with the user's map: prefer small, reversible steps and say what you changed.
        """;
}
