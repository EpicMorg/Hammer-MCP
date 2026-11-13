using System.Runtime.Versioning;
using HammerMcp.Server;
using HammerMcp.Shell;
using Microsoft.Extensions.Hosting;

namespace HammerMcp.Cli;

/// <summary>
/// hammer-mcp-cli: the MCP server without the tray app. Default: MCP over stdio (an MCP client starts it);
/// --http serves Streamable HTTP like the app. Picks the Hammer to work with by --pid or, with --auto, the first one
/// that has a shell channel. Without a Hammer (or off Windows) the server still starts and its tools say what is missing.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Program
{
    private const string Usage = """
        hammer-mcp-cli - MCP server for Valve Hammer Editor, without the tray app

          hammer-mcp-cli [--stdio] [--pid N | --auto] [--mode Auto|Basic|Shell] [--registry FILE]
          hammer-mcp-cli --http [--port 47800] [--bind 127.0.0.1] [--token T] [--pid N | --auto] [--mode ...]

          --stdio      MCP over stdin/stdout (default) - for clients that launch the server
          --http       Streamable HTTP on http://<bind>:<port>/mcp (a token is required beyond localhost)
          --pid N      work with the Hammer process N
          --auto       work with the first running Hammer that has a shell channel (Windows)
          --mode       Auto (default: live edits where possible), Basic (read-only), Shell
          --registry   hammers.json to use instead of the built-in registry
        """;

    public static async Task<int> Main(string[] args)
    {
        var http = false;
        var options = new HammerMcpServerOptions { RegistryPath = Path.Combine(AppContext.BaseDirectory, "registry", "hammers.json") };
        int? pid = null;
        var auto = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--stdio": http = false; break;
                case "--http": http = true; break;
                case "--port": options = options with { Port = int.Parse(Next()) }; break;
                case "--bind": options = options with { BindAddress = Next() }; break;
                case "--token": options = options with { Token = Next() }; break;
                case "--mode": options = options with { Mode = Enum.Parse<McpMode>(Next(), ignoreCase: true) }; break;
                case "--registry": options = options with { RegistryPath = Next() }; break;
                case "--pid": pid = int.Parse(Next()); break;
                case "--auto": auto = true; break;
                case "-h" or "--help" or "/?":
                    Console.Error.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"unknown argument {args[i]}\n\n{Usage}");
                    return 2;
            }
        }

        var log = new ActivityLog();
        // stdout belongs to the MCP protocol in stdio mode: everything human-readable goes to stderr
        log.Line += line => Console.Error.WriteLine(line);
        var target = new HammerTarget
        {
            AttachHint = OperatingSystem.IsWindows()
                ? "ask the user to start Hammer and restart hammer-mcp-cli with --auto (or --pid N)"
                : "Hammer runs on Windows; this hammer-mcp-cli instance has no Hammer to work with",
        };
        if (pid is { } p)
        {
            target.Attach(p);
            log.Write($"attached to pid {p}");
        }
        else if (auto && OperatingSystem.IsWindows() && HammerProcess.FindShellOwner() is { } found)
        {
            target.Attach(found.ProcessId);
            log.Write($"attached to {found.Name} ({found.ProcessId}) {found.ExePath}");
        }
        else
            log.Write("no Hammer attached (use --pid N or --auto); tools will report it");

        if (http)
        {
            var app = HammerMcpHost.Build(options, target, log);
            log.Write($"MCP over HTTP at {options.Url}");
            await app.RunAsync();
        }
        else
        {
            using var host = HammerMcpHost.BuildStdio(options, target, log);
            await host.RunAsync();
        }
        return 0;
    }
}
