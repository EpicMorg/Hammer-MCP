namespace HammerMcp.Server;

/// <summary>Network settings of the MCP endpoint (set from the tray app).</summary>
public sealed record HammerMcpServerOptions
{
    /// <summary>Loopback by default; LAN access is an explicit opt-in.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 47800;

    /// <summary>Path of the MCP endpoint.</summary>
    public string Path { get; init; } = "/mcp";

    /// <summary>Path to registry/hammers.json.</summary>
    public string RegistryPath { get; init; } = "registry/hammers.json";

    public string Url => $"http://{BindAddress}:{Port}{Path}";
}
