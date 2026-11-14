namespace HammerMcp.Server;

/// <summary>What the agent may do. Matches the "MCP Mode" combo in the app.</summary>
public enum McpMode
{
    /// <summary>
    /// Best available, never injects: the adapter if the user connected it (for what it supports), else shell + menu
    /// where a shell channel exists, else read-only.
    /// </summary>
    Auto,
    /// <summary>Read-only: status and map files, no live edits.</summary>
    Basic,
    /// <summary>Basic + live edits through the shell channel + menu commands (save, undo, run map...). Never the adapter.</summary>
    Shell,
    /// <summary>
    /// Strict, for debugging the adapter: everything that touches Hammer goes through the adapter only, an error without
    /// it. The adapter cannot edit yet (layer 2), so edits, menu commands and compiles are refused.
    /// </summary>
    Advanced,
}

/// <summary>Settings of the MCP endpoint (set from the tray app).</summary>
public sealed record HammerMcpServerOptions
{
    /// <summary>Loopback by default; LAN access is an explicit opt-in and requires a token.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 47800;

    /// <summary>Path of the MCP endpoint.</summary>
    public string Path { get; init; } = "/mcp";

    /// <summary>Bearer token; empty = no auth (only allowed on loopback).</summary>
    public string Token { get; init; } = "";

    public McpMode Mode { get; init; } = McpMode.Auto;

    /// <summary>Path to registry/hammers.json.</summary>
    public string RegistryPath { get; init; } = "registry/hammers.json";

    public bool IsLoopback => BindAddress is "127.0.0.1" or "localhost" or "::1";

    /// <summary>URL a client should use (0.0.0.0 is shown as the machine name).</summary>
    public string Url => $"http://{(BindAddress == "0.0.0.0" ? Environment.MachineName : BindAddress)}:{Port}{Path}";

    /// <summary>The edit tools are offered (in Advanced they exist but refuse: see <see cref="AdapterOnlyRefusal"/>).</summary>
    public bool AllowsLiveEdits => Mode is McpMode.Auto or McpMode.Shell or McpMode.Advanced;

    /// <summary>Edits / menu commands may use the shell channel and Win32 menu posting.</summary>
    public bool AllowsShell => Mode is McpMode.Auto or McpMode.Shell;

    /// <summary>Why an edit is refused in Advanced mode; null in the other modes.</summary>
    public string? AdapterOnlyRefusal => Mode == McpMode.Advanced
        ? "MCP Mode is Advanced: everything that changes Hammer goes through the adapter only, and the adapter cannot do "
          + "this yet - ask the user to switch MCP Mode to Auto (Settings) if they want you to edit"
        : null;
}
