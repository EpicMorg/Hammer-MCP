namespace HammerMcp.Core.Fgd;

/// <summary>An FGD class: @BaseClass / @PointClass / @SolidClass / @NPCClass / ... (also H++ @ExtendClass).</summary>
public sealed class FgdClass
{
    public required string Name { get; init; }

    /// <summary>BaseClass, PointClass, SolidClass, NPCClass, KeyFrameClass, MoveClass, FilterClass, ExtendClass, ...</summary>
    public required string Kind { get; init; }

    public string? Description { get; set; }

    public List<string> Bases { get; } = [];

    /// <summary>Editor helpers before '=': studio("..."), iconsprite("..."), size(...), color(...), line(...), ...</summary>
    public List<FgdBehaviour> Behaviours { get; } = [];

    public List<FgdProperty> Properties { get; } = [];

    public List<FgdIo> Inputs { get; } = [];

    public List<FgdIo> Outputs { get; } = [];

    /// <summary>Which file declared this class (last one wins when classes are redefined).</summary>
    public string? SourceFile { get; set; }

    public bool IsBase => Kind.Equals("BaseClass", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A helper like studio("models/x.mdl"); <see cref="Arguments"/> is the raw text between the parentheses.</summary>
public sealed record FgdBehaviour(string Name, string Arguments);

/// <summary>A keyvalue: name(type) [readonly] : "Display" : default : "Description" [= [ options ]].</summary>
public sealed class FgdProperty
{
    public required string Name { get; init; }

    /// <summary>string, integer, float, choices, flags, studio, target_destination, color255, ... (as written, lower-case).</summary>
    public required string Type { get; init; }

    public string? DisplayName { get; set; }
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
    public bool ReadOnly { get; set; }
    public bool Report { get; set; }
    public List<FgdOption> Options { get; } = [];
}

/// <summary>A choices / flags option: value : "Label" [: default-on for flags].</summary>
public sealed record FgdOption(string Value, string Label, bool? On);

public sealed record FgdIo(string Name, string Type, string? Description);

/// <summary>Result of parsing one FGD (with its @includes) or a whole game's FGD list.</summary>
public sealed class FgdDocument
{
    private readonly Dictionary<string, FgdClass> _classes = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<FgdClass> Classes => _classes.Values;

    public List<string> Includes { get; } = [];

    /// <summary>Problems the tolerant parser skipped over ("file:line: message").</summary>
    public List<string> Warnings { get; } = [];

    public (int Low, int High)? MapSize { get; set; }

    /// <summary>Hammer++ @version(n).</summary>
    public int? Version { get; set; }

    /// <summary>Which parser produced the classes: "sledge" or "hammermcp".</summary>
    public string Parser { get; set; } = "hammermcp";

    public FgdClass? Get(string name) => _classes.GetValueOrDefault(name);

    /// <summary>
    /// Adds a class. Like Valve's fgdlib, base classes are copied in at this moment (so a later class with the same
    /// name - FGD names are case-insensitive, e.g. @BaseClass Light vs @PointClass light - does not break inheritance);
    /// a later definition replaces an earlier one; an ExtendClass (Hammer++) merges into the existing class.
    /// </summary>
    public void Add(FgdClass cls)
    {
        if (cls.Kind.Equals("ExtendClass", StringComparison.OrdinalIgnoreCase))
        {
            if (_classes.TryGetValue(cls.Name, out var target))
                Merge(target, cls);
            return; // H++: an ExtendClass for an unknown class is ignored
        }
        _classes[cls.Name] = Flatten(cls);
    }

    private FgdClass Flatten(FgdClass cls)
    {
        if (cls.Bases.Count == 0)
            return cls;
        var flat = new FgdClass { Name = cls.Name, Kind = cls.Kind, Description = cls.Description, SourceFile = cls.SourceFile };
        flat.Bases.AddRange(cls.Bases);
        foreach (var baseName in cls.Bases)
        {
            if (_classes.TryGetValue(baseName, out var b))
                Merge(flat, b, keepDescription: true);
            else
                MissingBases.Add($"{cls.Name}: base '{baseName}' is not defined (yet)");
        }
        Merge(flat, cls);
        return flat;
    }

    /// <summary>Bases that were not defined when a class referenced them.</summary>
    public List<string> MissingBases { get; } = [];

    public void AddRange(FgdDocument other)
    {
        foreach (var c in other.Classes)
            Add(c);
        Warnings.AddRange(other.Warnings);
        Includes.AddRange(other.Includes);
        MapSize ??= other.MapSize;
        Version ??= other.Version;
    }

    /// <summary>A class with everything inherited from its bases (already flattened when it was added).</summary>
    public FgdClass? Resolve(string name) => Get(name);

    private static void Merge(FgdClass into, FgdClass from, bool keepDescription = false)
    {
        if (!keepDescription && !string.IsNullOrEmpty(from.Description))
            into.Description = from.Description;
        foreach (var b in from.Behaviours)
        {
            into.Behaviours.RemoveAll(x => x.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
            into.Behaviours.Add(b);
        }
        Replace(into.Properties, from.Properties, p => p.Name);
        Replace(into.Inputs, from.Inputs, p => p.Name);
        Replace(into.Outputs, from.Outputs, p => p.Name);
    }

    private static void Replace<T>(List<T> into, List<T> from, Func<T, string> key)
    {
        foreach (var item in from)
        {
            var i = into.FindIndex(x => key(x).Equals(key(item), StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                into[i] = item;
            else
                into.Add(item);
        }
    }
}
