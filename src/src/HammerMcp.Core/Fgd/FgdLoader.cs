using Sledge.Formats.FileSystem;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;

namespace HammerMcp.Core.Fgd;

/// <summary>
/// Loads FGDs. Our tolerant <see cref="FgdParser"/> is the default: it reads Hammer++ (@version, @ExtendClass), GMod (*types)
/// and odd GoldSrc files, and - like Valve's fgdlib - a redefined class replaces the earlier one. Sledge.Formats.GameData
/// merges redefinitions (duplicated choices, union of keyvalues), so it is kept as an alternative and as a test oracle.
/// </summary>
public static class FgdLoader
{
    /// <summary>One FGD file with its @includes.</summary>
    public static FgdDocument LoadFile(string path, bool preferSledge = false)
    {
        if (preferSledge && TrySledge(path, out var doc, out _))
            return doc!;
        return FgdParser.ParseFile(path);
    }

    /// <summary>A game's FGD list in Hammer's order (GameData0, GameData1, ...): later files override, ExtendClass merges.</summary>
    public static FgdDocument LoadGame(IEnumerable<string> paths)
    {
        var all = new FgdDocument();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                all.Warnings.Add($"{path}: not found");
                continue;
            }
            all.AddRange(LoadFile(path));
        }
        return all;
    }

    /// <summary>Parses with Sledge; false (with the error) if Sledge throws.</summary>
    public static bool TrySledge(string path, out FgdDocument? doc, out string? error)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            using var reader = File.OpenText(path);
            var gd = new FgdFormat(new DiskFileResolver(dir)) { AllowNewlinesInStrings = true }.Read(reader);
            doc = FromSledge(gd, Path.GetFileName(path));
            error = null;
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            doc = null;
            error = $"{e.GetType().Name}: {e.Message.Split('\n')[0]}";
            return false;
        }
    }

    private static FgdDocument FromSledge(GameDefinition gd, string fileName)
    {
        var doc = new FgdDocument { Parser = "sledge" };
        doc.Includes.AddRange(gd.Includes);
        doc.Warnings.AddRange(gd.Warnings.Select(w => $"{fileName}: {w}"));
        if (gd.MapSizeLow is { } lo && gd.MapSizeHigh is { } hi)
            doc.MapSize = (lo, hi);
        foreach (var c in gd.Classes)
        {
            var cls = new FgdClass { Name = c.Name, Kind = c.ClassType.ToString(), Description = c.Description, SourceFile = fileName };
            cls.Bases.AddRange(c.BaseClasses);
            cls.Behaviours.AddRange(c.Behaviours.Select(b => new FgdBehaviour(b.Name, string.Join(", ", b.Values))));
            foreach (var p in c.Properties)
            {
                var prop = new FgdProperty
                {
                    Name = p.Name,
                    Type = (p.UnknownVariableTypeName ?? p.VariableType.ToString()).ToLowerInvariant(),
                    DisplayName = p.Description,
                    DefaultValue = p.DefaultValue,
                    Description = p.Details,
                };
                prop.Options.AddRange(p.Options.Select(o => new FgdOption(o.Key, o.Description, o.On)));
                cls.Properties.Add(prop);
            }
            foreach (var io in c.InOuts)
            {
                var item = new FgdIo(io.Name, (io.UnknownVariableTypeName ?? io.VariableType.ToString()).ToLowerInvariant(), io.Description);
                (io.IOType == IOType.Input ? cls.Inputs : cls.Outputs).Add(item);
            }
            doc.Add(cls);
        }
        return doc;
    }
}
