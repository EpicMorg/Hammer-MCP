using System.ComponentModel;
using System.Runtime.Versioning;
using HammerMcp.Core.Fgd;
using HammerMcp.Core.Maps;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

/// <summary>Reading the saved map and the game's FGD: entities with outputs, I/O graph, entity class reference, validation.</summary>
[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class MapTools(HammerTarget target, GameData gameData)
{
    [McpServerTool(Name = "game_configs", ReadOnly = true)]
    [Description("Hammer's game configurations (from GameConfig.txt / hammerplusplus_gameconfig.txt next to the attached Hammer): FGD files, game dir, map dir, compile tools; and which one applies to the open map.")]
    public object GameConfigs()
    {
        var s = gameData.Select();
        return new
        {
            configFile = s.ConfigFile,
            active = s.Active?.Name,
            problem = s.Problem,
            configs = s.Configs.Select(c => new { c.Name, fgd = c.FgdFiles, c.GameDir, c.MapDir, c.BspDir, tools = c.Tools }),
        };
    }

    [McpServerTool(Name = "fgd_class", ReadOnly = true)]
    [Description("Reference for an entity class from the game's FGD (with everything inherited from base classes): description, keyvalues (type, default, choices / flags), inputs, outputs, editor helpers.")]
    public object FgdClass(
        [Description("Entity class, e.g. light, trigger_multiple, prop_dynamic")] string className,
        [Description("Game config name if Hammer has several (see game_configs)")] string? configName = null)
    {
        var (fgd, error) = LoadFgd(configName);
        if (fgd is null)
            return new { error };
        var cls = fgd.Resolve(className);
        if (cls is null)
            return new { error = $"class '{className}' is not in the FGD", similar = Similar(fgd, className) };
        return new
        {
            cls.Name,
            kind = cls.Kind,
            cls.Description,
            bases = cls.Bases,
            helpers = cls.Behaviours.Select(b => $"{b.Name}({b.Arguments})"),
            keyvalues = cls.Properties.Select(p => new
            {
                p.Name,
                p.Type,
                display = p.DisplayName,
                @default = p.DefaultValue,
                p.Description,
                readOnly = p.ReadOnly ? true : (bool?)null,
                options = p.Options.Count == 0 ? null : p.Options.Select(o => o.On is null ? $"{o.Value} = {o.Label}" : $"{o.Value} = {o.Label}{(o.On == true ? " (on)" : "")}"),
            }),
            inputs = cls.Inputs.Select(i => new { i.Name, i.Type, i.Description }),
            outputs = cls.Outputs.Select(o => new { o.Name, o.Type, o.Description }),
            source = cls.SourceFile,
        };
    }

    [McpServerTool(Name = "fgd_search", ReadOnly = true)]
    [Description("Find entity classes in the game's FGD whose name or description contains the text (e.g. 'door', 'spawn', 'sound'). Base classes are excluded.")]
    public object FgdSearch(string text, int limit = 30, string? configName = null)
    {
        var (fgd, error) = LoadFgd(configName);
        if (fgd is null)
            return new { error };
        var hits = fgd.Classes
            .Where(c => !c.IsBase && (c.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || c.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(c => c.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(c => c.Name)
            .ToList();
        return new
        {
            total = hits.Count,
            classes = hits.Take(Math.Clamp(limit, 1, 200)).Select(c => new
            {
                c.Name,
                kind = c.Kind,
                description = c.Description is { Length: > 160 } d ? d[..160] + "…" : c.Description,
            }),
        };
    }

    [McpServerTool(Name = "get_entity", ReadOnly = true)]
    [Description("One entity of the SAVED map by id (from list_entities): all keyvalues, its outputs, and outputs of other entities that target it by name.")]
    public object GetEntity(int id, [Description("Read Hammer++'s newest autosave if it is newer than the saved map")] bool fromAutosave = false)
    {
        var (entities, error) = LoadMap(fromAutosave);
        if (entities is null)
            return new { error };
        var e = entities.FirstOrDefault(x => x.Id == id);
        if (e is null)
            return new { error = $"no entity with id {id} in the saved map" };
        return new
        {
            source = _source,
            e.Id,
            className = e.ClassName,
            origin = e.Origin is { } o ? new[] { o.X, o.Y, o.Z } : null,
            e.Hidden,
            brushEntity = e.HasBrushes,
            keyValues = e.KeyValues,
            outputs = e.Connections.Select(Format),
            targetedBy = e.TargetName is null ? [] : Inbound(entities, e.TargetName),
        };
    }

    [McpServerTool(Name = "find_io", ReadOnly = true)]
    [Description("Map logic around a name in the SAVED map: entities with that targetname, their outputs, and every output that targets the name (wildcards like door_* in targets are matched).")]
    public object FindIo(string targetName, [Description("Read Hammer++'s newest autosave if it is newer than the saved map")] bool fromAutosave = false)
    {
        var (entities, error) = LoadMap(fromAutosave);
        if (entities is null)
            return new { error };
        var named = entities.Where(e => string.Equals(e.TargetName, targetName, StringComparison.OrdinalIgnoreCase)).ToList();
        return new
        {
            source = _source,
            entities = named.Select(e => new { e.Id, className = e.ClassName, outputs = e.Connections.Select(Format) }),
            targetedBy = Inbound(entities, targetName),
        };
    }

    [McpServerTool(Name = "validate_map", ReadOnly = true)]
    [Description("Checks the SAVED map against the game's FGD: unknown entity classes, outputs to names that do not exist, output / input names the FGD does not know, keyvalues missing from the FGD. Returns problems grouped by kind (capped by 'limit').")]
    public object ValidateMap(int limit = 50, string? configName = null, bool fromAutosave = false)
    {
        var (entities, error) = LoadMap(fromAutosave);
        if (entities is null)
            return new { error };
        var (fgd, fgdError) = LoadFgd(configName);
        if (fgd is null)
            return new { error = fgdError };

        var r = MapValidator.Validate(entities, fgd);
        var cap = Math.Clamp(limit, 1, 1000);
        return new
        {
            source = _source,
            entities = entities.Count,
            fgdClasses = fgd.Classes.Count(),
            unknownClasses = new { count = r.UnknownClasses.Count, items = r.UnknownClasses.Take(cap) },
            outputsToMissingTargets = new { count = r.MissingTargets.Count, items = r.MissingTargets.Take(cap) },
            unknownOutputNames = new { count = r.UnknownOutputs.Count, items = r.UnknownOutputs.Take(cap) },
            inputsTargetDoesNotHave = new { count = r.UnknownInputs.Count, items = r.UnknownInputs.Take(cap) },
            keyValuesNotInFgd = new { count = r.UnknownKeyValues.Count, items = r.UnknownKeyValues.Take(cap) },
            fgdWarnings = fgd.Warnings.Take(20),
        };
    }

    private static List<object> Inbound(List<VmfEntity> entities, string name) =>
        [.. entities.SelectMany(e => e.Connections
                .Where(c => string.Equals(c.Target, name, StringComparison.OrdinalIgnoreCase)
                            || (c.Target.EndsWith('*') && name.StartsWith(c.Target[..^1], StringComparison.OrdinalIgnoreCase)))
                .Select(c => (object)new { fromId = e.Id, fromClass = e.ClassName, fromName = e.TargetName, connection = Format(c) }))];

    private static string Format(VmfConnection c) => MapValidator.Format(c);

    private static IEnumerable<string> Similar(FgdDocument fgd, string name) =>
        fgd.Classes.Where(c => !c.IsBase && (c.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(c.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Name).Take(10);

    private (FgdDocument? Fgd, string? Error) LoadFgd(string? configName)
    {
        var s = gameData.Select(configName);
        if (s.Active is null)
            return (null, s.Problem);
        if (s.Active.FgdFiles.Count == 0)
            return (null, $"game config '{s.Active.Name}' lists no FGD files");
        return (gameData.Fgd(s.Active), null);
    }

    private (List<VmfEntity>? Entities, string? Error) LoadMap(bool fromAutosave = false)
    {
        var (path, source) = target.MapSource(fromAutosave);
        _source = source;
        return path is null ? (null, "no saved map open in the attached Hammer") : (MapEntities.Read(path), null);
    }

    private string _source = "";
}
