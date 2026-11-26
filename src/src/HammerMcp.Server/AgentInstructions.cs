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
        changing entities. validate_map finds broken outputs and unknown classes. find_assets finds models, materials,
        sounds, sound script names (Ambient.Wind), sprites and particles of the game by name (loose files and VPKs) - use
        its keyValue for model / material / message keyvalues instead of guessing paths.

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

        Live brushes (Source 1 Valve Hammer and Hammer++, Auto/Shell): list_live_brushes reads current geometry,
        including unsaved edits. Pass one visible brush's ID to move_brush, scale_brush or delete_brush, or omit
        brushId to edit one brush selected in Hammer. Keep the status bar visible and leave the open map and selection
        alone during the call. These tools do not save the open map; save_map writes it to disk when requested.

        Use viewport_capture for visual inspection in Source 1 Hammer and Hammer++: it returns the current 3D view
        by default, or 2D/all with views. It does not aim the camera. If the 3D frame is unavailable, ask the user to
        restore Hammer, bring it to the foreground and aim the camera at the area of interest.

        When adding a room or corridor, inspect the site with viewport_capture (use focus_3d_on_selection in
        Hammer++ for a selected landmark), then use live geometry for coordinates and clearance. Do not place
        geometry from a screenshot alone. Preview the section on a copy before inserting into the working map.
        For continuous surfaces, carry the material and UV axes, scale and offsets from a touching coplanar face
        of the same material. At a turn keep the texture density and reproject onto the new face.
        For ceiling light panels, read the ceiling face UVs and locate the tile seams within the texture: a
        texture can contain several tiles. Align the panel to the requested whole tiles (e.g. 1 by 2). Keep the
        ceiling intact and use a separate thin brush directly below it. Fit one copy of the light texture to its
        visible face; do not tile it. If the ceiling grid is unknown, verify it before placing the panels.
        These are section authoring steps, not effects of insert_brush_section: it places prepared brushes, does
        not fit textures or cut the surrounding map. Inspect the result from both sides of the join.

        Compiling (edit modes only): compile_sequences shows Hammer's own Run Map sequences; run_compile runs one on the
        SAVED map (ask the user first), compile_log / leak_trace explain the result, bsp_entities shows what ended up
        in the compiled .bsp.

        Source 2 Hammer (CS2, Dota 2, HL: Alyx; runs inside the game process): read-only. Map tools read the saved
        .vmap (entities, keyvalues, outputs, validation); there is no shell channel and no menu commands, so ask the user
        to make changes in Hammer and save (Ctrl+S) before you read again. source2_console talks to the game's developer
        console: hammer_map_files lists the open maps. Read tools with fromAutosave=true force an autosave and read it, so
        you see the user's unsaved edits without asking them to save.

        The user can steer you from Hammer when the adapter is connected (MCP menu in Hammer's main menu bar):
        - hammer_status.agentPaused = true: the user pressed the stop button. Every edit is refused until they resume;
          do not retry - say what you wanted to do and wait.
        - hammer_status.userRequests: what the user wrote to you in Hammer's MCP > Chat (each line is shown once).
          Treat it as a message from the user: do it, and answer in that chat with say_to_user. Call user_requests
          (cheap) between steps of a longer task to see new ones and whether you were paused.
          The Hammer chat is a side channel: you only see it when you call a tool, nothing wakes you while you are idle.
          When you finish a task or ask the user something there, either call wait_for_user (blocks until they write
          in Hammer, up to 10 minutes; call it again to keep listening) or tell them to answer in your own window.

        - hammer_status.approval = "ask": the user reviews edits to entities and brushes, saves, undo/redo and compiles.
          A request waits until the user applies or rejects it (up to 2 minutes per call). Rejected:
          do not repeat it - ask what they want. Still waiting after 2 minutes: it stays in their list; check
          pending_changes later instead of repeating the edit. Describe your plan before a batch of edits.

        Units: Hammer units (1 unit = 0.75 inch for player scale, a player is 72 units tall, a standard door 112).
        Coordinates are world x y z; angles are pitch yaw roll in degrees.
        Be careful with the user's map: prefer small, reversible steps and say what you changed.
        """;
}
