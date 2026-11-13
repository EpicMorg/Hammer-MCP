using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.Versioning;
using HammerMcp.Core.Source2;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>
/// Source 2 Hammer through the game's own developer console (VConsole2, TCP 127.0.0.1:29000) - the official channel
/// vconsole2.exe uses; nothing is injected. Only an allowlist of harmless commands is accepted.
/// </summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class Source2Tools(HammerTarget target, ActivityLog log)
{
    /// <summary>Commands (first word) the agent may run: Hammer info, forcing an autosave, help / search.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "hammer_map_files", "hammer_prefab_tree", "hammer_force_autosave", "hammer_commands",
        "find", "help", "status", "version", "echo",
    };

    [McpServerTool(Name = "source2_console")]
    [Description("Source 2 Hammer (CS2, Dota 2, HL:A) only: runs one allowlisted command in the game's developer console (VConsole2, TCP 127.0.0.1:29000) and returns the printed text. Allowed: hammer_map_files (maps open in Hammer), hammer_force_autosave (writes an autosave with unsaved edits), hammer_prefab_tree, hammer_commands, find <text>, help <command>, status, version, echo. If the connection is refused, ask the user to close vconsole2.exe (it may hold the single connection) or start the game with -vconsole.")]
    public async Task<object> Source2Console(
        [Description("Console command, e.g. hammer_map_files")] string command,
        [Description("Seconds to collect the output")] double waitSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        var process = target.Current();
        if (process is null)
            return new { error = "no Hammer is attached" };
        if (!process.IsSource2)
            return new { error = "source2_console works only with Source 2 Hammer (CS2, Dota 2, HL:A)" };
        var word = command.Trim().Split(' ', 2)[0];
        if (!Allowed.Contains(word) || command.Contains(';') || command.Contains('\n'))
            return new { error = $"'{word}' is not allowed (one command, no ';')", allowed = Allowed.Order() };
        try
        {
            using var client = await VConsoleClient.ConnectAsync(ct: cancellationToken);
            var output = await client.RunAsync(command.Trim(), TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0.5, 15)), cancellationToken);
            log.Write($"vconsole: {command.Trim()} -> {output.Length} chars");
            return new { command = command.Trim(), output, game = client.AppInfo };
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException)
        {
            return new
            {
                error = $"VConsole at 127.0.0.1:{VConsoleClient.DefaultPort} not reachable: {e.Message}",
                hint = "close vconsole2.exe if it is open (one client at a time) or add -vconsole to the game's launch options",
            };
        }
    }
}
