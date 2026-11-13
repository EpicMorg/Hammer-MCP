using System.Collections;
using System.Globalization;
using System.Numerics;
using Datamodel;

namespace HammerMcp.Core.Maps;

/// <summary>
/// Source 2 Hammer maps (.vmap = DMX, usually binary v9, format "vmap"), read with Datamodel.NET (KeyValues2 package).
/// The node tree under <c>world.children</c> holds CMapEntity / CMapMesh / CMapGroup / CMapPrefab / CMapInstance...;
/// node transforms are relative to the parent, so origins here are converted to world space.
/// Entities come out as <see cref="VmfEntity"/> so the Source 1 tools (list, get, I/O graph, validation) work unchanged.
/// Prefabs (CMapPrefab → targetMapPath, another .vmap) and instances (CMapInstance → a group) are expanded with their
/// transforms; their entities get ids from <see cref="PrefabIdBase"/> and a "prefab" keyvalue.
/// </summary>
public static class VmapMap
{
    public sealed record Result(VmfSummary Summary, List<VmfEntity> Entities);

    /// <summary>Nested prefabs deeper than this are not expanded (Hammer allows nesting; this guards against cycles too).</summary>
    public const int MaxPrefabDepth = 6;

    /// <summary>
    /// Entities of prefabs / instances get ids from here up (their own nodeIDs would collide with the map's);
    /// the original prefab node and file are kept in the "prefab" / "prefab_node" keyvalues.
    /// </summary>
    public const int PrefabIdBase = 1_000_000;

    /// <summary>True for .vmap files and anything that starts like a DMX file (Source 2 autosaves may use other names).</summary>
    public static bool IsVmap(string path)
    {
        if (Path.GetExtension(path).Equals(".vmap", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[16];
            var n = f.Read(head);
            return System.Text.Encoding.ASCII.GetString(head, 0, n).StartsWith("<!-- dmx", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, (DateTime Written, long Size, bool Expanded, Result Result)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <see cref="Read(string, bool)"/> with a small cache keyed by path + write time + size (a big map with prefabs takes
    /// seconds to read; tools call this for every request). Prefab files changing alone do not invalidate it.
    /// </summary>
    public static Result ReadCached(string path, bool expandPrefabs = true)
    {
        var info = new FileInfo(path);
        var key = Path.GetFullPath(path);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var c) && c.Written == info.LastWriteTimeUtc && c.Size == info.Length && c.Expanded == expandPrefabs)
                return c.Result;
        }
        var result = Read(path, expandPrefabs);
        lock (CacheGate)
        {
            if (Cache.Count >= 8)
                Cache.Clear();
            Cache[key] = (info.LastWriteTimeUtc, info.Length, expandPrefabs, result);
        }
        return result;
    }

    public static Result Read(string path, bool expandPrefabs = true)
    {
        using var dm = Datamodel.Datamodel.Load(path, Datamodel.Codecs.DeferredMode.Disabled);
        return Read(dm, expandPrefabs ? path : null);
    }

    /// <param name="mapPath">Path of the map: needed to resolve prefab files (maps/prefabs/x.vmap in the same mod / addon); null = do not expand prefabs.</param>
    public static Result Read(Datamodel.Datamodel dm, string? mapPath = null)
    {
        var root = dm.Root ?? throw new InvalidDataException("vmap without a root element");
        var world = G<Element>(root, "world") ?? throw new InvalidDataException("vmap without a world element");

        var entities = new List<VmfEntity>();
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        int meshes = 0, worldMeshes = 0, brushEntities = 0, hiddenCount = 0, prefabs = 0, nextId = PrefabIdBase;
        var opened = new List<Datamodel.Datamodel>();
        var relays = new List<VmfEntity>();

        void Walk(Element node, Element ownWorld, HashSet<Element> hidden, Matrix4x4 parent, bool parentHidden, bool inEntity, bool direct,
                  string? file, (string Path, int Node)? prefab, int depth, HashSet<string> chain, Dictionary<string, string>? vars = null)
        {
            var transform = Local(node) * parent;
            var isHidden = parentHidden || hidden.Contains(node) || Attr<bool>(node, "force_hidden");
            if (isHidden && !parentHidden && prefab is null)
                hiddenCount++;
            switch (node.ClassName)
            {
                case "CMapMesh":
                    meshes++;
                    if (!inEntity && direct && prefab is null)
                        worldMeshes++;
                    break;
                case "CMapEntity":
                {
                    var props = G<Element>(node, "entity_properties");
                    var className = G<string>(props, "classname") ?? "?";
                    var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (props is not null)
                        foreach (var a in props)
                            kv[a.Key] = Text(a.Value);
                    // keyvalues bound to prefab variables (variableTargetKeys[i] ← variableNames[i]) take the variable's value
                    if (vars is not null && G<StringArray>(node, "variableTargetKeys") is { } bound && G<StringArray>(node, "variableNames") is { } names)
                    {
                        for (var i = 0; i < bound.Count && i < names.Count; i++)
                            if (bound[i] is { Length: > 0 } key && names[i] is { } varName && vars.TryGetValue(varName, out var value))
                                kv[key] = value;
                    }
                    var origin = Vector3.Transform(Vector3.Zero, transform);
                    kv["origin"] = Text(origin);
                    if (node.ContainsKey("angles"))
                        kv["angles"] = Text(G<QAngle>(node, "angles"));
                    var hasMesh = Children(node).Any(c => c.ClassName == "CMapMesh");
                    if (hasMesh)
                        brushEntities++;
                    var id = prefab is null ? Attr<int>(node, "nodeID") : nextId++;
                    kv["id"] = id.ToString(CultureInfo.InvariantCulture);
                    if (prefab is { } p)
                    {
                        kv["prefab"] = p.Path;
                        kv["prefab_node"] = p.Node.ToString(CultureInfo.InvariantCulture);
                    }
                    entities.Add(new VmfEntity(id, className, origin, kv.GetValueOrDefault("targetname") is { Length: > 0 } tn ? tn : null,
                        isHidden, hasMesh, kv) { Connections = Connections(node) });
                    classes[className] = classes.GetValueOrDefault(className) + 1;
                    foreach (var c in Children(node))
                        Walk(c, ownWorld, hidden, transform, isHidden, inEntity: true, direct: false, file, prefab, depth, chain, vars);
                    return;
                }
                case "CMapPrefab":
                {
                    prefabs++;
                    var target = G<string>(node, "targetMapPath");
                    if (file is null || string.IsNullOrEmpty(target) || depth >= MaxPrefabDepth || ResolvePrefab(file, target) is not { } sub
                        || !chain.Add(Path.GetFullPath(sub)))
                        break;
                    try
                    {
                        var subDm = Datamodel.Datamodel.Load(sub, Datamodel.Codecs.DeferredMode.Disabled);
                        opened.Add(subDm);
                        if (subDm.Root is { } subRoot && G<Element>(subRoot, "world") is { } subWorld)
                        {
                            var mark = prefab ?? (target, Attr<int>(node, "nodeID"));
                            var subHidden = HiddenNodes(subRoot);
                            var at = Local(subWorld) * transform;
                            var subVars = PrefabVariables(subRoot, node);
                            foreach (var c in Children(subWorld))
                                Walk(c, subWorld, subHidden, at, isHidden, inEntity, direct: false, sub, mark, depth + 1, chain, subVars);
                            // a named prefab is an I/O target of the outer map: its world relays the inputs inside (relayPlugData)
                            if (G<string>(node, "targetName") is { Length: > 0 } prefabName)
                            {
                                var origin = Vector3.Transform(Vector3.Zero, transform);
                                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["classname"] = "worldspawn", ["targetname"] = prefabName, ["origin"] = Text(origin),
                                    ["prefab"] = target!, ["prefab_node"] = Attr<int>(node, "nodeID").ToString(CultureInfo.InvariantCulture),
                                };
                                var id = nextId++;
                                kv["id"] = id.ToString(CultureInfo.InvariantCulture);
                                relays.Add(new VmfEntity(id, "worldspawn", origin, prefabName, isHidden, false, kv) { Connections = Connections(subWorld) });
                            }
                        }
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or Datamodel.CodecException or UnauthorizedAccessException)
                    {
                        // a broken or missing prefab file: the map itself still reads
                    }
                    finally
                    {
                        chain.Remove(Path.GetFullPath(sub));
                    }
                    break;
                }
                case "CMapInstance":
                {
                    // a placed copy of a group (or prefab) of the same map: walk the target's children at the instance's transform
                    if (G<Element>(node, "target") is { } instanceOf && depth < MaxPrefabDepth)
                    {
                        var mark = prefab ?? ($"instance of node {Attr<int>(instanceOf, "nodeID")}", Attr<int>(node, "nodeID"));
                        foreach (var c in Children(instanceOf))
                            Walk(c, ownWorld, hidden, transform, isHidden, inEntity, direct: false, file, mark, depth + 1, chain, vars);
                    }
                    break;
                }
            }
            foreach (var c in Children(node))
                Walk(c, ownWorld, hidden, transform, isHidden, inEntity, direct: node == ownWorld, file, prefab, depth, chain, vars);
        }

        try
        {
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mapPath is not null)
                chain.Add(Path.GetFullPath(mapPath));
            var rootHidden = HiddenNodes(root);
            foreach (var c in Children(world))
                Walk(c, world, rootHidden, Local(world), parentHidden: false, inEntity: false, direct: true, mapPath, null, 0, chain);
        }
        finally
        {
            foreach (var d in opened)
                d.Dispose();
        }

        var worldProps = G<Element>(world, "entity_properties");
        // the world itself can be an I/O target (prefabs relay their inputs / outputs through it), as in the engine;
        // listed last and not counted in the summary
        List<VmfEntity>? entitiesWithWorld = null;
        if (worldProps is not null)
        {
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in worldProps)
                kv[a.Key] = Text(a.Value);
            var worldId = Attr<int>(world, "nodeID");
            kv["id"] = worldId.ToString(CultureInfo.InvariantCulture);
            var worldEntity = new VmfEntity(worldId, kv.GetValueOrDefault("classname") ?? "worldspawn", null,
                kv.GetValueOrDefault("targetname") is { Length: > 0 } wn ? wn : null, false, worldMeshes > 0, kv) { Connections = Connections(world) };
            entitiesWithWorld = [.. entities, .. relays, worldEntity];
        }
        var summary = new VmfSummary
        {
            EditorVersion = Attr<int?>(root, "editorversion"),
            EditorBuild = Attr<int?>(root, "editorbuild"),
            FormatVersion = dm.FormatVersion,
            Prefab = Attr<bool>(root, "isprefab"),
            SkyName = G<string>(worldProps, "skyname") is { Length: > 0 } sky ? sky : null,
            Entities = entities.Count,
            PointEntities = entities.Count - brushEntities,
            BrushEntities = brushEntities,
            WorldBrushes = worldMeshes,
            Brushes = meshes,
            EntityClasses = classes.Count,
            HiddenObjects = hiddenCount,
            Prefabs = prefabs,
            PrefabEntities = entities.Count(e => e.Id >= PrefabIdBase),
            Classes = [.. classes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)],
        };
        return new Result(summary, entitiesWithWorld ?? [.. entities, .. relays]);
    }

    /// <summary>
    /// Variables of a placed prefab: the prefab map's defaults (mapVariables: variableNames / variableValues) overridden
    /// by the placing node (variableOverrideNames / variableOverrideValues).
    /// </summary>
    private static Dictionary<string, string> PrefabVariables(Element prefabRoot, Element placement)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var set = G<Element>(prefabRoot, "mapVariables");
        if (G<StringArray>(set, "variableNames") is { } names && G<StringArray>(set, "variableValues") is { } values)
            for (var i = 0; i < names.Count && i < values.Count; i++)
                if (names[i] is { } n)
                    vars[n] = values[i] ?? "";
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (G<StringArray>(placement, "variableOverrideNames") is { } on && G<StringArray>(placement, "variableOverrideValues") is { } ov)
            for (var i = 0; i < on.Count && i < ov.Count; i++)
                if (on[i] is { } n)
                    overrides[n] = ov[i] ?? "";
        // choice groups (e.g. TowerLocation = middle_tier_3) set several variables at once; the placement picks the choice
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in G<ElementArray>(set, "m_ChoiceGroups")?.OfType<Element>() ?? [])
        {
            groups.Add(group.Name ?? "");
            var picked = overrides.GetValueOrDefault(group.Name ?? "") ?? G<string>(group, "m_ActiveValue");
            if (G<ElementArray>(group, "m_Choices")?.OfType<Element>().FirstOrDefault(c => string.Equals(c.Name, picked, StringComparison.OrdinalIgnoreCase)) is not { } choice)
                continue;
            if (G<StringArray>(group, "m_ChoiceVariables") is { } cv && G<StringArray>(choice, "m_ChoiceValues") is { } values2)
                for (var i = 0; i < cv.Count && i < values2.Count; i++)
                    if (cv[i] is { } n)
                        vars[n] = values2[i] ?? "";
        }
        foreach (var (n, v) in overrides)
            if (!groups.Contains(n))
                vars[n] = v;
        return vars;
    }

    /// <summary>
    /// File of a prefab's targetMapPath ("maps/prefabs/x.vmap"): relative to the mod / addon folder that holds the map's
    /// "maps" folder (content/dota/, content/csgo_addons/my_addon/), then to the stock mod folder (content/csgo/).
    /// </summary>
    public static string? ResolvePrefab(string mapPath, string targetMapPath)
    {
        var rel = targetMapPath.Replace('\\', '/').TrimStart('/');
        // an autosave (content\_toolsautosave\hammer\...) uses the prefabs of the real map's folder
        mapPath = Source2Content.FromAutosaveTree(Path.GetFullPath(mapPath));
        var dir = Path.GetDirectoryName(mapPath);
        while (dir is not null && !Path.GetFileName(dir).Equals("maps", StringComparison.OrdinalIgnoreCase))
            dir = Path.GetDirectoryName(dir);
        var modDir = dir is null ? null : Path.GetDirectoryName(dir);
        var candidates = new List<string>();
        if (modDir is not null)
        {
            candidates.Add(Path.Combine(modDir, rel));
            // an addon (content/csgo_addons/x) also sees the stock mod's content (content/csgo)
            var parent = Path.GetDirectoryName(modDir);
            if (parent is not null && Path.GetFileName(parent).EndsWith("_addons", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(parent) is { } content)
                candidates.Add(Path.Combine(content, Path.GetFileName(parent)[..^"_addons".Length], rel));
        }
        candidates.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? ".", Path.GetFileName(rel)));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static List<VmfConnection> Connections(Element entity)
    {
        var list = new List<VmfConnection>();
        if (G<ElementArray>(entity, "connectionsData") is not { } data)
            return list;
        foreach (var c in data.OfType<Element>())
        {
            list.Add(new VmfConnection(
                G<string>(c, "outputName") ?? "",
                G<string>(c, "targetName") ?? "",
                G<string>(c, "inputName") ?? "",
                G<string>(c, "overrideParam") ?? "",
                Attr<float>(c, "delay"),
                c.ContainsKey("timesToFire") ? Attr<int>(c, "timesToFire") : -1));
        }
        return list;
    }

    /// <summary>Nodes hidden in Hammer's visibility manager ("visbility" - sic - with nodes[] / hiddenFlags[]).</summary>
    private static HashSet<Element> HiddenNodes(Element root)
    {
        var set = new HashSet<Element>();
        var vis = G<Element>(root, "visbility") ?? G<Element>(root, "visibility");
        if (G<ElementArray>(vis, "nodes") is { } nodes && G<IntArray>(vis, "hiddenFlags") is { } flags)
        {
            for (var i = 0; i < nodes.Count && i < flags.Count; i++)
                if (flags[i] != 0 && nodes[i] is { } n)
                    set.Add(n);
        }
        return set;
    }

    private static T? G<T>(Element? e, string name) => e is not null && e.TryGetValue(name, out var v) && v is T t ? t : default;

    private static IEnumerable<Element> Children(Element node) =>
        G<ElementArray>(node, "children")?.OfType<Element>() ?? [];

    private static T? Attr<T>(Element e, string name)
    {
        if (!e.ContainsKey(name))
            return default;
        var v = e[name];
        if (v is T t)
            return t;
        try
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return v is IConvertible ? (T)Convert.ChangeType(v, target, CultureInfo.InvariantCulture) : default;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }

    /// <summary>Node transform: scale, then Source angles (pitch Y, yaw Z, roll X), then translation; row-vector convention.</summary>
    private static Matrix4x4 Local(Element node)
    {
        var origin = G<Vector3>(node, "origin");
        var scales = node.ContainsKey("scales") ? G<Vector3>(node, "scales") : Vector3.One;
        var a = G<QAngle>(node, "angles");
        float Rad(float d) => d * MathF.PI / 180f;
        var (sp, cp) = MathF.SinCos(Rad(a.Pitch));
        var (sy, cy) = MathF.SinCos(Rad(a.Yaw));
        var (sr, cr) = MathF.SinCos(Rad(a.Roll));
        var forward = new Vector3(cp * cy, cp * sy, -sp);
        var left = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
        var up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        var rotation = new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0,
            left.X, left.Y, left.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        return Matrix4x4.CreateScale(scales) * rotation * Matrix4x4.CreateTranslation(origin);
    }

    private static string Num(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Text(Vector3 v) => $"{Num(v.X)} {Num(v.Y)} {Num(v.Z)}";

    private static string Text(QAngle q) => $"{Num(q.Pitch)} {Num(q.Yaw)} {Num(q.Roll)}";

    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "1" : "0",
        float f => Num(f),
        Vector3 v => Text(v),
        QAngle q => Text(q),
        Element e => e.Name ?? e.ClassName,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable list => string.Join(' ', list.Cast<object?>().Select(Text)),
        _ => value.ToString() ?? "",
    };
}
