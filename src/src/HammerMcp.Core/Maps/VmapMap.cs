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
/// Prefabs and instances referencing other .vmap files are not expanded.
/// </summary>
public static class VmapMap
{
    public sealed record Result(VmfSummary Summary, List<VmfEntity> Entities);

    public static Result Read(string path)
    {
        using var dm = Datamodel.Datamodel.Load(path, Datamodel.Codecs.DeferredMode.Disabled);
        return Read(dm);
    }

    public static Result Read(Datamodel.Datamodel dm)
    {
        var root = dm.Root ?? throw new InvalidDataException("vmap without a root element");
        var world = G<Element>(root, "world") ?? throw new InvalidDataException("vmap without a world element");

        var hidden = HiddenNodes(root);
        var entities = new List<VmfEntity>();
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        int meshes = 0, worldMeshes = 0, brushEntities = 0, hiddenCount = 0;

        void Walk(Element node, Matrix4x4 parent, bool parentHidden, bool inEntity, bool direct)
        {
            var transform = Local(node) * parent;
            var isHidden = parentHidden || hidden.Contains(node) || Attr<bool>(node, "force_hidden");
            if (isHidden && !parentHidden)
                hiddenCount++;
            switch (node.ClassName)
            {
                case "CMapMesh":
                    meshes++;
                    if (!inEntity && direct)
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
                    var origin = Vector3.Transform(Vector3.Zero, transform);
                    kv["origin"] = Text(origin);
                    if (node.ContainsKey("angles"))
                        kv["angles"] = Text(G<QAngle>(node, "angles"));
                    var hasMesh = Children(node).Any(c => c.ClassName == "CMapMesh");
                    if (hasMesh)
                        brushEntities++;
                    var id = Attr<int>(node, "nodeID");
                    kv["id"] = id.ToString(CultureInfo.InvariantCulture);
                    entities.Add(new VmfEntity(id, className, origin, kv.GetValueOrDefault("targetname") is { Length: > 0 } tn ? tn : null,
                        isHidden, hasMesh, kv) { Connections = Connections(node) });
                    classes[className] = classes.GetValueOrDefault(className) + 1;
                    foreach (var c in Children(node))
                        Walk(c, transform, isHidden, inEntity: true, direct: false);
                    return;
                }
            }
            foreach (var c in Children(node))
                Walk(c, transform, isHidden, inEntity, direct: node == world);
        }

        foreach (var c in Children(world))
            Walk(c, Local(world), parentHidden: false, inEntity: false, direct: true);

        var worldProps = G<Element>(world, "entity_properties");
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
            Classes = [.. classes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)],
        };
        return new Result(summary, entities);
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
