

namespace HammerMcp.Server;

/// <summary>
/// Ready-to-paste MCP client settings for the running server (tray: Copy client config). The token, if any, is put in
/// as a bearer header. Formats follow each client's docs: Claude Code (claude mcp add), Codex (config.toml),
/// OpenCode (opencode.json), Oh My Pi (.omp/mcp.json), Gemini CLI (settings.json, httpUrl = streamable HTTP),
/// Cursor (mcp.json), VS Code (.vscode/mcp.json), Claude Desktop (through mcp-remote).
/// </summary>
public static class ClientConfigs
{
    public static IReadOnlyList<(string Client, Func<HammerMcpServerOptions, string> Text)> All { get; } =
    [
        ("Claude Code", o => $"claude mcp add --transport http hammer {o.Url}"
                             + (Has(o) ? $" --header \"Authorization: Bearer {o.Token}\"" : "")),
        ("Codex (~/.codex/config.toml)", o => $"[mcp_servers.hammer]\nurl = \"{o.Url}\"\n"
                             + (Has(o) ? $"http_headers = {{ \"Authorization\" = \"Bearer {o.Token}\" }}\n" : "")),
        ("OpenCode (opencode.json)", o => Json($$"""
            {
              "$schema": "https://opencode.ai/config.json",
              "mcp": {
                "hammer": {
                  "type": "remote",
                  "url": "{{o.Url}}",
                  "oauth": false{{Headers(o, 6)}}
                }
              }
            }
            """)),
        ("Oh My Pi (.omp/mcp.json)", o => Json($$"""
            {
              "mcpServers": {
                "hammer": {
                  "type": "http",
                  "url": "{{o.Url}}"{{Headers(o, 6)}}
                }
              }
            }
            """)),
        ("Gemini CLI (~/.gemini/settings.json)", o => Json($$"""
            {
              "mcpServers": {
                "hammer": {
                  "httpUrl": "{{o.Url}}"{{Headers(o, 6)}}
                }
              }
            }
            """)),
        ("Cursor (~/.cursor/mcp.json)", o => Json($$"""
            {
              "mcpServers": {
                "hammer": {
                  "url": "{{o.Url}}"{{Headers(o, 6)}}
                }
              }
            }
            """)),
        ("VS Code / Copilot (.vscode/mcp.json)", o => Json($$"""
            {
              "servers": {
                "hammer": {
                  "type": "http",
                  "url": "{{o.Url}}"{{Headers(o, 6)}}
                }
              }
            }
            """)),
        ("Claude Desktop (via mcp-remote, needs Node.js)", o => Json($$"""
            {
              "mcpServers": {
                "hammer": {
                  "command": "npx",
                  "args": ["-y", "mcp-remote", "{{o.Url}}"{{(Has(o) ? $", \"--header\", \"Authorization: Bearer {o.Token}\"" : "")}}]
                }
              }
            }
            """)),
    ];

    private static bool Has(HammerMcpServerOptions o) => !string.IsNullOrEmpty(o.Token);

    private static string Headers(HammerMcpServerOptions o, int indent) =>
        Has(o) ? $",\n{new string(' ', indent)}\"headers\": {{ \"Authorization\": \"Bearer {o.Token}\" }}" : "";

    private static string Json(string text) => text.Replace("\r\n", "\n");
}
