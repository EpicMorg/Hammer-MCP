namespace HammerMcp.Server;

/// <summary>What the agent may do. Matches the "MCP Mode" combo in the app.</summary>
public enum McpMode
{
    /// <summary>Best available: shell + menu where a shell channel exists, else read-only.</summary>
    Auto,
    /// <summary>Read-only: status and map files, no live edits.</summary>
    Basic,
    /// <summary>Basic + live edits through the shell channel + menu commands (save, undo, run map...).</summary>
    Shell,
    /// <summary>Shell + injected adapter (expert). Not implemented yet: behaves as Shell.</summary>
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

    public bool AllowsLiveEdits => Mode is McpMode.Auto or McpMode.Shell or McpMode.Advanced;
}
