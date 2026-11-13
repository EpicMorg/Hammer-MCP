using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using HammerMcp.Core.Assets;
using HammerMcp.Core.Compile;
using HammerMcp.Core.Config;
using HammerMcp.Core.Maps;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>Content checks and the compile pipeline: Run Map sequences, compile logs, leaks, running a compile.</summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class CompileTools(HammerTarget target, GameData gameData)
{
    [McpServerTool(Name = "check_assets", ReadOnly = true)]
    [Description("Content the SAVED map uses (brush materials, models, overlays, decals, sprites, skybox) checked against the game's search paths from gameinfo.txt, folders and VPKs. Returns what is missing, grouped by kind.")]
    public object CheckAssets(int limit = 50, string? configName = null)
    {
        var (map, config, error) = MapAndConfig(configName);
        if (error is not null)
            return new { error };
        var resolver = gameData.Assets(config!);
        if (resolver is null)
            return new { error = $"no gameinfo.txt in GameDir '{config!.GameDir}'" };
        var refs = MapAssets.Collect(map!);
        var missing = refs.Where(r => !resolver.Exists(r.Path)).ToList();
        return new
        {
            checkedAssets = refs.Count,
            missing = missing.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => new
            {
                count = g.Count(),
                items = g.Take(Math.Clamp(limit, 1, 500)).Select(r => new { r.Path, r.Uses, entityId = r.ExampleEntityId }),
            }),
            searchFolders = resolver.Folders,
            vpks = resolver.Vpks,
            searchPathProblems = resolver.Problems,
        };
    }

    [McpServerTool(Name = "compile_sequences", ReadOnly = true)]
    [Description("Hammer's Run Map (expert) sequences of the attached Hammer - CmdSeq.wc, hammerplusplus_sequences.cfg or Strata cmdseq.wc - with variables expanded for the open map.")]
    public object Sequences(string? configName = null)
    {
        var process = target.Current();
        if (process is null)
            return new { error = "no Hammer is attached" };
        var (file, sequences) = CompileSequences.ForExe(process.ExePath);
        var (_, config, _) = MapAndConfig(configName);
        var vars = Variables(process.ExePath, process.OpenMapPath, config);
        return new
        {
            file,
            sequences = sequences.Select(s => new
            {
                s.Name,
                commands = s.Commands.Select(c => new
                {
                    c.Enabled,
                    kind = c.Kind.ToString(),
                    run = CompileSequences.Expand(c.Run, vars),
                    parameters = CompileSequences.ExpandArguments(c.Parameters, vars),
                    c.NoWait,
                }),
            }),
        };
    }

    [McpServerTool(Name = "compile_log", ReadOnly = true)]
    [Description("Parses the compile log of the open map (<map>.log next to the .vmf, or a given path): stages with timings, leaks, missing materials, model errors, limits, warnings, errors (deduplicated with counts).")]
    public object Log(string? logPath = null, int limit = 60)
    {
        var path = logPath ?? (target.Current()?.OpenMapPath is { } map ? Path.ChangeExtension(map, ".log") : null);
        if (path is null || !File.Exists(path))
            return new { error = $"compile log not found: {path ?? "(no map open)"}" };
        var parsed = CompileLog.Read(path);
        return new
        {
            path,
            written = File.GetLastWriteTime(path),
            parsed.Leaked,
            leakedEntities = parsed.LeakedEntities.Distinct(),
            stages = parsed.Stages.Select(s => new { s.Name, s.Seconds }),
            issues = parsed.Issues.Take(Math.Clamp(limit, 1, 500)).Select(i => new { kind = i.Kind.ToString(), i.Stage, i.Message, i.Count, line = i.FirstLine }),
            totalIssues = parsed.Issues.Count,
        };
    }

    [McpServerTool(Name = "leak_trace", ReadOnly = true)]
    [Description("The leak path from the pointfile (<map>.lin) written by vbsp when the map leaks: points from the void to the leaking entity. Use the open_run_map_dialog / menu Map > Load Pointfile to show it in Hammer.")]
    public object LeakTrace()
    {
        var map = target.Current()?.OpenMapPath;
        var lin = map is null ? null : Path.ChangeExtension(map, ".lin");
        if (lin is null || !File.Exists(lin))
            return new { leaked = false, note = "no .lin next to the map: the last compile did not leak (or vbsp was not run)" };
        var trace = HammerMcp.Core.Compile.LeakTrace.Read(lin);
        return new
        {
            leaked = true,
            path = lin,
            written = File.GetLastWriteTime(lin),
            points = trace.Points.Select(p => new[] { p.X, p.Y, p.Z }),
            lengthUnits = Math.Round(trace.Length),
            leakEnd = trace.Points.LastOrDefault() is var e ? new[] { e.X, e.Y, e.Z } : null,
        };
    }

    internal static object Result(string sequence, List<object> steps, string map, DateTime started, string status)
    {
        var logPath = Path.ChangeExtension(map, ".log");
        // a log left over from an earlier compile would describe the wrong run
        var fresh = File.Exists(logPath) && File.GetLastWriteTime(logPath) >= started.AddSeconds(-2);
        var parsed = fresh ? CompileLog.Read(logPath) : null;
        return new
        {
            sequence,
            status,
            steps,
            logNote = fresh ? null : File.Exists(logPath) ? $"{logPath} was not written by this run (older log ignored)" : $"no {logPath}",
            log = parsed is null ? null : new
            {
                path = logPath,
                parsed.Leaked,
                stages = parsed.Stages.Select(s => new { s.Name, s.Seconds }),
                issues = parsed.Issues.Take(40).Select(i => new { kind = i.Kind.ToString(), i.Stage, i.Message, i.Count }),
            },
        };
    }

    internal static async Task<(int? Code, string Tail)> RunProcess(string exe, string args, string cwd, bool noWait, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = Directory.Exists(cwd) ? cwd : Path.GetDirectoryName(exe) ?? ".",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = !noWait,
            RedirectStandardError = !noWait,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {exe}");
        if (noWait)
            return (null, "started, not waiting");
        var output = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            return (-1, "killed: timeout or cancelled");
        }
        string text;
        lock (output)
            text = output.ToString();
        var lines = text.Split('\n');
        return (p.ExitCode, string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 15))).Trim());
    }

    /// <summary>"a b" or "\"a b\" \"c d\"" → two paths.</summary>
    internal static (string, string) TwoPaths(string args)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in args)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (ch == ' ' && !quoted)
            {
                if (sb.Length > 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
                sb.Append(ch);
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString());
        return parts.Count == 2 ? (parts[0], parts[1]) : throw new InvalidOperationException($"expected two paths: {args}");
    }

    /// <summary>Hammer's Run Map variables for a map and game config.</summary>
    internal static Dictionary<string, string> Variables(string hammerExe, string? mapPath, HammerGameConfig? config)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exedir"] = Path.GetDirectoryName(hammerExe) ?? "",
        };
        if (mapPath is not null)
        {
            v["path"] = Path.GetDirectoryName(mapPath) ?? "";
            v["file"] = Path.GetFileNameWithoutExtension(mapPath);
            v["ext"] = Path.GetExtension(mapPath).TrimStart('.');
        }
        if (config is not null)
        {
            void Set(string var, string key)
            {
                if (config.Tools.TryGetValue(key, out var val) && val.Length > 0)
                    v[var] = val;
            }
            Set("bsp_exe", "BSP");
            Set("vis_exe", "Vis");
            Set("light_exe", "Light");
            Set("game_exe", "GameExe");
            Set("csg_exe", "CSG");
            if (config.GameDir is { } g)
            {
                v["gamedir"] = g;
                v["gamename"] = Path.GetFileName(g.TrimEnd('\\', '/'));
            }
            if (config.BspDir is { } b)
                v["bspdir"] = b;
        }
        return v;
    }

    [McpServerTool(Name = "bsp_entities", ReadOnly = true)]
    [Description("Entities as they ended up in a COMPILED map (.bsp entity lump): what vbsp actually wrote, with outputs as repeated keys. Default file: the open map's .bsp next to the .vmf, else <GameDir>/maps/<name>.bsp. Filter by classname / targetname (prefix with *), or pass a .bsp path.")]
    public object BspEntitiesTool(string? className = null, string? targetName = null, string? bspPath = null, int limit = 50, string? configName = null)
    {
        var path = bspPath;
        if (path is null)
        {
            var map = target.Current()?.OpenMapPath;
            if (map is null)
                return new { error = "no map open in the attached Hammer; pass bspPath" };
            path = Path.ChangeExtension(map, ".bsp");
            if (!File.Exists(path) && gameData.Select(configName).Active is { GameDir: { } gameDir })
                path = Path.Combine(gameDir, "maps", Path.GetFileName(path));
        }
        if (!File.Exists(path))
            return new { error = $"no {path}" };
        BspEntities.Result bsp;
        try
        {
            bsp = BspEntities.Read(path);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            return new { error = e.Message, path };
        }
        static bool Like(string? value, string? pattern) => pattern is null || (value is not null &&
            (pattern.EndsWith('*') ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase) : value.Equals(pattern, StringComparison.OrdinalIgnoreCase)));
        var hits = bsp.Entities.Where(e => Like(e.ClassName, className) && Like(e.Get("targetname"), targetName)).ToList();
        return new
        {
            path,
            written = File.GetLastWriteTime(path),
            bspVersion = bsp.Version,
            total = bsp.Entities.Count,
            matched = hits.Count,
            entities = hits.Take(Math.Clamp(limit, 1, 500)).Select(e => new
            {
                e.Index,
                e.ClassName,
                keyValues = e.KeyValues.Where(kv => VmfConnection.Parse(kv.Key, kv.Value) is null).Select(kv => $"{kv.Key} = {kv.Value}"),
                outputs = e.KeyValues.Select(kv => VmfConnection.Parse(kv.Key, kv.Value)).OfType<VmfConnection>().Select(MapValidator.Format),
            }),
        };
    }

    internal (string? Map, HammerGameConfig? Config, string? Error) MapAndConfig(string? configName)
    {
        var map = target.Current()?.OpenMapPath;
        if (map is null || !File.Exists(map))
            return (null, null, "no saved map open in the attached Hammer");
        var s = gameData.Select(configName);
        return s.Active is null ? (map, null, s.Problem) : (map, s.Active, null);
    }
}
