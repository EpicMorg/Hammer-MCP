using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Pe;
using HammerMcp.Core.Registry;
using ModelContextProtocol.Server;

namespace HammerMcp.Server.Tools;

[McpServerToolType]
[SupportedOSPlatform("windows")]
public sealed class StatusTools(HammerRegistry registry, HammerTarget target, HammerMcpServerOptions options)
{
    private static readonly string[] EditorDlls = ["hammerplusplus_dll.dll", "hammer_dll.dll", "hammer.dll"];

    [McpServerTool(Name = "hammer_status", ReadOnly = true)]
    [Description("Which Hammer the server works with (process, exe, known build), which map is open, its saved mapversion, and what the agent may do (mode).")]
    public HammerStatus Status()
    {
        var process = target.Current();
        if (process is null)
            return new HammerStatus(Running: false, Mode: options.Mode.ToString(), LiveEdits: false,
                Note: "no Hammer is attached - ask the user to pick the Hammer process in Hammer-MCP and press Attach");

        var mapPath = process.OpenMapPath;
        int? mapVersion = mapPath is not null && File.Exists(mapPath) ? VmfVersionInfo.Read(mapPath).MapVersion : null;
        var (build, dll) = Identify(registry, process.ExePath);
        return new HammerStatus(
            Running: true,
            Mode: options.Mode.ToString(),
            LiveEdits: options.AllowsLiveEdits && process.HasShellChannel,
            ProcessId: process.ProcessId,
            ExePath: process.ExePath,
            Arch: PeHeader.TryRead(process.ExePath)?.Arch,
            EditorDll: dll,
            BuildId: build?.Id,
            Family: build?.Family,
            ShellChannel: process.HasShellChannel,
            OpenMap: mapPath,
            SavedMapVersion: mapVersion);
    }

    [McpServerTool(Name = "map_summary", ReadOnly = true)]
    [Description("Summary of the map open in Hammer, read from the SAVED .vmf: mapversion, editor build, entity and brush counts, entity classes with counts, skybox, visgroups. Unsaved edits are not included - call save_map first.")]
    public object MapSummary([Description("Read Hammer++'s newest autosave if it is newer than the saved map")] bool fromAutosave = false)
    {
        var (map, source) = target.MapSource(fromAutosave);
        if (map is null)
            return new { error = "no saved map open in the attached Hammer" };
        var file = new FileInfo(map);
        return new { path = map, source, saved = file.LastWriteTime, sizeBytes = file.Length, summary = VmfSummary.Read(map) };
    }

    [McpServerTool(Name = "list_entities", ReadOnly = true)]
    [Description("Entities of the map open in Hammer, from the SAVED .vmf: id, class, origin, targetname, hidden, brush entity. Filters are optional and combined; results are capped by 'limit'.")]
    public object ListEntities(
        [Description("Exact class name, e.g. light")] string? className = null,
        [Description("Substring of targetname")] string? nameContains = null,
        [Description("Only entities within 'radius' units of x y z")] float? x = null, float? y = null, float? z = null,
        float radius = 256,
        int limit = 200,
        [Description("Read Hammer++'s newest autosave if it is newer than the saved map")] bool fromAutosave = false)
    {
        var (map, source) = target.MapSource(fromAutosave);
        if (map is null)
            return new { error = "no saved map open in the attached Hammer (or nothing attached)" };
        var center = x is { } cx && y is { } cy && z is { } cz ? new System.Numerics.Vector3(cx, cy, cz) : (System.Numerics.Vector3?)null;
        var query = MapEntities.Read(map).AsEnumerable();
        if (className is not null)
            query = query.Where(e => string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase));
        if (nameContains is not null)
            query = query.Where(e => e.TargetName?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) == true);
        if (center is { } c)
            query = query.Where(e => e.Origin is { } o && System.Numerics.Vector3.Distance(o, c) <= radius);
        var all = query.ToList();
        return new
        {
            map,
            source,
            total = all.Count,
            entities = all.Take(Math.Clamp(limit, 1, 2000)).Select(e => new
            {
                e.Id,
                className = e.ClassName,
                origin = e.Origin is { } o ? new[] { o.X, o.Y, o.Z } : null,
                targetName = e.TargetName,
                e.Hidden,
                brushEntity = e.HasBrushes,
            }),
        };
    }

    /// <summary>Finds the editor DLL next to the exe and looks its sha256 up in the registry.</summary>
    public static (HammerBuild? Build, string? Dll) Identify(HammerRegistry registry, string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null)
            return (null, null);
        foreach (var name in EditorDlls)
        {
            // Hammer++ keeps its DLL in bin/hammerplusplus/bin next to hammerplusplus.exe; Source 2 in bin/win64/tools
            foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "hammerplusplus", "bin", name), Path.Combine(dir, "tools", name) })
            {
                if (File.Exists(candidate))
                    return (registry.FindBySha256(Sha256Of(candidate)), candidate);
            }
        }
        // GoldSrc Worldcraft / Hammer 3.x: the editor is the exe itself
        return File.Exists(exePath) && registry.FindBySha256(Sha256Of(exePath)) is { } own ? (own, exePath) : (null, null);
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public sealed record HammerStatus(
    bool Running,
    string Mode,
    bool LiveEdits,
    int? ProcessId = null,
    string? ExePath = null,
    string? Arch = null,
    string? EditorDll = null,
    string? BuildId = null,
    string? Family = null,
    bool ShellChannel = false,
    string? OpenMap = null,
    int? SavedMapVersion = null,
    string? Note = null);
