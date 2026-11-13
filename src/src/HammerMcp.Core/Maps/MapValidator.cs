using HammerMcp.Core.Fgd;

namespace HammerMcp.Core.Maps;

/// <summary>Checks map entities against an FGD: unknown classes, outputs to missing names, unknown output / input names, extra keyvalues.</summary>
public static class MapValidator
{
    /// <summary>Targets resolved by the engine at runtime, not by name in the map.</summary>
    private static readonly HashSet<string> SpecialTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "!self", "!activator", "!caller", "!player", "!pvsplayer", "!speechtarget", "!picker", "!friend", "!enemy",
    };

    /// <summary>Keyvalues every entity may carry, whether or not its FGD declares them.</summary>
    private static readonly HashSet<string> CommonKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "classname", "origin", "angles", "spawnflags", "targetname", "parentname", "hammerid", "model",
        // Source 2 editor keys present on every entity
        "local.origin", "local.angles", "local.scales", "useLocalOffset", "parentAttachmentName", "vscripts",
    };

    public sealed record Issue(int Id, string ClassName, string Detail);

    public sealed record Result(
        List<Issue> UnknownClasses,
        List<Issue> MissingTargets,
        List<Issue> UnknownOutputs,
        List<Issue> UnknownInputs,
        List<Issue> UnknownKeyValues,
        List<Issue> QuotesInsideValues);

    public static Result Validate(IReadOnlyList<VmfEntity> entities, FgdDocument fgd)
    {
        var names = entities.Where(e => e.TargetName is not null).GroupBy(e => e.TargetName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // the engine falls back to the classname when no entity has the target name (e.g. "point_servercommand")
        var classes = entities.GroupBy(e => e.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, FgdClass?>(StringComparer.OrdinalIgnoreCase);
        FgdClass? Cls(string name) => resolved.TryGetValue(name, out var c) ? c : resolved[name] = fgd.Resolve(name);

        var r = new Result([], [], [], [], [], []);
        foreach (var e in entities)
        {
            foreach (var line in e.BrokenLines)
                r.QuotesInsideValues.Add(new Issue(e.Id, e.ClassName, line));
            var cls = Cls(e.ClassName);
            if (cls is null)
            {
                r.UnknownClasses.Add(new Issue(e.Id, e.ClassName, ""));
                continue;
            }
            // Source 2 prefabs: the world carries user-named relay plugs (X_Input / X_Output) that no FGD describes
            var isWorld = e.ClassName.Equals("worldspawn", StringComparison.OrdinalIgnoreCase);
            foreach (var key in isWorld ? [] : e.KeyValues.Keys)
            {
                if (!CommonKeys.Contains(key) && !cls.Properties.Any(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    r.UnknownKeyValues.Add(new Issue(e.Id, e.ClassName, key));
            }
            foreach (var c in e.Connections)
            {
                if (!isWorld && !cls.Outputs.Any(o => o.Name.Equals(c.Output, StringComparison.OrdinalIgnoreCase)))
                    r.UnknownOutputs.Add(new Issue(e.Id, e.ClassName, c.Output));
                var targets = Targets(names, c.Target) ?? Targets(classes, c.Target)
                    ?? (Cls(c.Target) is { IsBase: false } ? [] : null); // a class spawned at runtime (player, ...)
                if (targets is null)
                {
                    r.MissingTargets.Add(new Issue(e.Id, e.ClassName, Format(c)));
                    continue;
                }
                foreach (var t in targets.Select(t => t.ClassName).Distinct(StringComparer.OrdinalIgnoreCase)
                             .Where(t => !t.Equals("worldspawn", StringComparison.OrdinalIgnoreCase)))
                {
                    if (Cls(t) is { } tc && !tc.Inputs.Any(i => i.Name.Equals(c.Input, StringComparison.OrdinalIgnoreCase)))
                        r.UnknownInputs.Add(new Issue(e.Id, e.ClassName, $"{Format(c)} (target is {t})"));
                }
            }
        }
        return r;
    }

    public static string Format(VmfConnection c) =>
        $"{c.Output} -> {c.Target}.{c.Input}({c.Parameter}) delay {c.Delay.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} times {c.TimesToFire}";

    /// <summary>Entities a target refers to; empty for engine-resolved targets (!player...); null if nothing matches.</summary>
    private static List<VmfEntity>? Targets(Dictionary<string, List<VmfEntity>> names, string target)
    {
        if (SpecialTargets.Contains(target) || target.StartsWith('!'))
            return [];
        if (target.EndsWith('*'))
        {
            var prefix = target[..^1];
            var hits = names.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).SelectMany(kv => kv.Value).ToList();
            return hits.Count > 0 ? hits : null;
        }
        return names.GetValueOrDefault(target);
    }
}
