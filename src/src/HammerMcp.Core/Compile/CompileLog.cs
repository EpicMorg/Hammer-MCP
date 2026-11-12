using System.Globalization;
using System.Text.RegularExpressions;

namespace HammerMcp.Core.Compile;

/// <summary>Kinds of findings in a vbsp / vvis / vrad log.</summary>
public enum CompileIssueKind
{
    Leak,
    MissingMaterial,
    ModelError,
    Limit,
    Error,
    Warning,
}

public sealed record CompileIssue(CompileIssueKind Kind, string Stage, string Message, int Count, int FirstLine);

public sealed record CompileStage(string Name, int StartLine, double? Seconds);

/// <summary>Parsed compile log (the &lt;map&gt;.log the compilers append to): stages, leaks, missing assets, limits, errors.</summary>
public sealed partial record CompileLog(IReadOnlyList<CompileStage> Stages, IReadOnlyList<CompileIssue> Issues, IReadOnlyList<string> LeakedEntities, bool Leaked)
{
    public static CompileLog Read(string path) => Parse(File.ReadAllLines(path));

    public static CompileLog Parse(IReadOnlyList<string> lines)
    {
        var stages = new List<CompileStage>();
        var issues = new Dictionary<(CompileIssueKind, string, string), (int Count, int First)>();
        var leaked = new List<string>();
        var leak = false;
        var stage = "?";

        void Add(CompileIssueKind kind, string message, int line)
        {
            var key = (kind, stage, message);
            issues[key] = issues.TryGetValue(key, out var v) ? (v.Count + 1, v.First) : (1, line);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            if (StageRegex().Match(line) is { Success: true } sm)
            {
                stage = (sm.Groups[1].Success ? sm.Groups[1] : sm.Groups[2]).Value.ToLowerInvariant();
                stages.Add(new CompileStage(stage, i + 1, null));
                continue;
            }
            if (ElapsedRegex().Match(line) is { Success: true } em && stages.Count > 0 && stages[^1].Seconds is null)
            {
                stages[^1] = stages[^1] with { Seconds = double.Parse(em.Groups[1].Value, CultureInfo.InvariantCulture) };
                continue;
            }
            if (line.Contains("**** leaked ****", StringComparison.OrdinalIgnoreCase) || line.Contains("LEAKED!", StringComparison.Ordinal) && !line.Contains("Entity"))
            {
                leak = true;
                Add(CompileIssueKind.Leak, "map leaked: vvis / vrad will be poor, load the pointfile (.lin) to see the leak path", i + 1);
                continue;
            }
            if (LeakedEntityRegex().Match(line) is { Success: true } lm)
            {
                leak = true;
                leaked.Add(lm.Groups[1].Value);
                Add(CompileIssueKind.Leak, $"Entity {lm.Groups[1].Value} leaked", i + 1);
                continue;
            }
            if (MaterialRegex().Match(line) is { Success: true } mm)
            {
                Add(CompileIssueKind.MissingMaterial, mm.Groups[1].Success ? mm.Groups[1].Value.ToLowerInvariant() : mm.Groups[2].Value.ToLowerInvariant(), i + 1);
                continue;
            }
            if (line.Contains("studio model", StringComparison.OrdinalIgnoreCase) || line.Contains("VTX file checksum", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Error! To use model", StringComparison.OrdinalIgnoreCase))
            {
                Add(CompileIssueKind.ModelError, line, i + 1);
                continue;
            }
            if (LimitRegex().IsMatch(line))
            {
                Add(CompileIssueKind.Limit, line, i + 1);
                continue;
            }
            if (line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) || line.Contains("WARNING:", StringComparison.Ordinal))
            {
                Add(CompileIssueKind.Warning, line, i + 1);
                continue;
            }
            if (ErrorRegex().IsMatch(line))
                Add(CompileIssueKind.Error, line, i + 1);
        }

        var list = issues.Select(kv => new CompileIssue(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.Count, kv.Value.First))
            .OrderBy(x => x.Kind).ThenBy(x => x.FirstLine).ToList();
        return new CompileLog(stages, list, leaked, leak);
    }

    // "Valve Software - vbsp.exe (...)", "============ Started VBSP compilation", "vrad.exe ..."
    [GeneratedRegex(@"(?:Valve Software - |^)(vbsp|vvis|vrad|bspzip|vbspinfo)\.exe|Started (VBSP|VVIS|VRAD) compilation", RegexOptions.IgnoreCase)]
    private static partial Regex StageRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?) seconds elapsed", RegexOptions.IgnoreCase)]
    private static partial Regex ElapsedRegex();

    [GeneratedRegex(@"^Entity (\S+) \([-\d. ]+\) leaked", RegexOptions.IgnoreCase)]
    private static partial Regex LeakedEntityRegex();

    [GeneratedRegex(@"Material not found!?:\s*(\S+)|material ""([^""]+)"" not found", RegexOptions.IgnoreCase)]
    private static partial Regex MaterialRegex();

    [GeneratedRegex(@"MAX_MAP_|exceeded|Too many|overflow|Ran out of", RegexOptions.IgnoreCase)]
    private static partial Regex LimitRegex();

    [GeneratedRegex(@"\berror\b|could not|couldn't|failed|unable to", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorRegex();
}

/// <summary>A pointfile (.lin): the leak path from outside the map to the leaking entity.</summary>
public sealed record LeakTrace(IReadOnlyList<System.Numerics.Vector3> Points)
{
    public double Length => Points.Zip(Points.Skip(1), System.Numerics.Vector3.Distance).Sum();

    public static LeakTrace Read(string path)
    {
        var points = new List<System.Numerics.Vector3>();
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                points.Add(new System.Numerics.Vector3(x, y, z));
        }
        return new LeakTrace(points);
    }
}
