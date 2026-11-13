using System.ComponentModel;
using System.Runtime.Versioning;
using HammerMcp.Core.Compile;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>Running a compile changes files (the .bsp, the game's maps folder), so it is offered only when edits are allowed.</summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class RunCompileTools(HammerTarget target, CompileTools reader, ActivityLog log)
{
    [McpServerTool(Name = "run_compile", Destructive = true)]
    [Description("Runs one of Hammer's Run Map sequences for the SAVED map (vbsp / vvis / vrad, copy .bsp to the game's maps folder) and returns the parsed compile log. Ask the user first: it takes time and overwrites the .bsp. Steps that launch the game are skipped unless launchGame is true. Save the map (save_map) before compiling.")]
    public async Task<object> RunCompile(
        [Description("Sequence name from compile_sequences, e.g. Default or Fast")] string sequence,
        bool launchGame = false,
        int timeoutMinutes = 30,
        string? configName = null,
        CancellationToken cancellationToken = default)
    {
        var process = target.Current();
        var (map, config, error) = reader.MapAndConfig(configName);
        if (process is null || error is not null)
            return new { error = error ?? "no Hammer is attached" };
        var (_, sequences) = CompileSequences.ForExe(process.ExePath);
        var seq = sequences.FirstOrDefault(s => s.Name.Equals(sequence, StringComparison.OrdinalIgnoreCase));
        if (seq is null)
            return new { error = $"no sequence '{sequence}'", available = sequences.Select(s => s.Name) };

        var vars = CompileTools.Variables(process.ExePath, map, config);
        var steps = new List<object>();
        var cwd = Path.GetDirectoryName(map)!;
        var started = DateTime.Now;
        foreach (var cmd in seq.Commands.Where(c => c.Enabled))
        {
            var run = CompileSequences.Expand(cmd.Run, vars).Trim();
            var args = CompileSequences.ExpandArguments(cmd.Parameters, vars).Trim();
            var isGame = vars.TryGetValue("game_exe", out var gameExe) && run.Equals(gameExe, StringComparison.OrdinalIgnoreCase);
            if ((isGame || cmd.NoWait) && !launchGame)
            {
                steps.Add(new { step = $"{run} {args}", skipped = "launches the game (launchGame=false)" });
                continue;
            }
            log.Write($"compile [{seq.Name}]: {cmd.Kind} {run} {args}");
            try
            {
                switch (cmd.Kind)
                {
                    case CompileCommandKind.ChangeDirectory:
                        cwd = (args.Length > 0 ? args : run).Trim('"');
                        steps.Add(new { step = $"cd {cwd}", ok = Directory.Exists(cwd) });
                        break;
                    case CompileCommandKind.CopyFile:
                    {
                        var (from, to) = CompileTools.TwoPaths(args);
                        File.Copy(from, to, overwrite: true);
                        steps.Add(new { step = $"copy {from} -> {to}", ok = true });
                        break;
                    }
                    case CompileCommandKind.DeleteFile:
                        File.Delete(args.Trim('"'));
                        steps.Add(new { step = $"delete {args}", ok = true });
                        break;
                    case CompileCommandKind.RenameFile:
                    {
                        var (from, to) = CompileTools.TwoPaths(args);
                        File.Move(from, to, overwrite: true);
                        steps.Add(new { step = $"rename {from} -> {to}", ok = true });
                        break;
                    }
                    default:
                    {
                        var (code, tail) = await CompileTools.RunProcess(run, args, cwd, cmd.NoWait, TimeSpan.FromMinutes(Math.Clamp(timeoutMinutes, 1, 240)), cancellationToken);
                        steps.Add(new { step = $"{run} {args}", exitCode = code, outputTail = tail });
                        if (code is not 0 and not null)
                            return CompileTools.Result(seq.Name, steps, map!, started, $"stopped: {Path.GetFileName(run)} exited with {code}");
                        break;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                steps.Add(new { step = $"{cmd.Kind} {run} {args}", error = e.Message });
                return CompileTools.Result(seq.Name, steps, map!, started, "stopped on error");
            }
        }
        return CompileTools.Result(seq.Name, steps, map!, started, "done");
    }

}
