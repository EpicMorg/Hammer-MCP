using System.Text.Json;
using System.Text.Json.Nodes;

namespace HammerMcp.Core.Registry;

/// <summary>
/// What is known about a Hammer build's user interface (registry/ui/&lt;build id&gt;.json, embedded): menus, accelerators,
/// toolbars, dialogs with their control ids (dumped from the build's resources by registry/tools/dump_ui.py) and
/// recipes verified live (how to create a block, which dialog field is the hollow thickness, what freezes Hammer...).
/// A build with the same UI as another one points to its recipes with "recipesFrom".
/// </summary>
public sealed class UiKnowledge
{
    private readonly JsonObject _doc;
    private readonly JsonObject? _recipes;

    private UiKnowledge(string build, JsonObject doc, JsonObject? recipes, string? recipesBuild)
    {
        Build = build;
        _doc = doc;
        _recipes = recipes;
        RecipesBuild = recipesBuild;
    }

    public string Build { get; }

    /// <summary>The build whose live-verified recipes apply (this one or its "recipesFrom"); null = none yet.</summary>
    public string? RecipesBuild { get; }

    /// <summary>Build ids that have a UI file.</summary>
    public static IReadOnlyList<string> Known() =>
        [.. typeof(UiKnowledge).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("ui/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[3..^5]).Order(StringComparer.Ordinal)];

    /// <summary>The UI knowledge of a build, null if there is none.</summary>
    public static UiKnowledge? For(string buildId)
    {
        if (Read(buildId) is not { } doc)
            return null;
        var recipes = doc["recipes"] as JsonObject;
        var recipesBuild = recipes is not null ? buildId : null;
        if (recipes is null && (string?)doc["recipesFrom"] is { } from && Read(from)?["recipes"] is JsonObject inherited)
        {
            recipes = inherited;
            recipesBuild = from;
        }
        return new UiKnowledge(buildId, doc, recipes, recipesBuild);
    }

    private static JsonObject? Read(string buildId)
    {
        using var stream = typeof(UiKnowledge).Assembly.GetManifestResourceStream($"ui/{buildId}.json");
        return stream is null ? null : JsonNode.Parse(stream)?.AsObject();
    }

    /// <summary>Names of the recipes (procedures verified live).</summary>
    public IReadOnlyList<string> RecipeNames => _recipes is null ? [] : [.. _recipes.Select(r => r.Key)];

    /// <summary>
    /// Everything whose name or text contains every word of the query (case-insensitive): recipes, menu items,
    /// commands (status / tooltip text), accelerators and dialogs (title or a control's text). Empty query = recipe
    /// names and the dialog titles only.
    /// </summary>
    public JsonObject Search(string? query, int limit = 30)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Hit(JsonNode? n) => words.Length > 0 && n is not null && words.All(w => n.ToJsonString().Contains(w, StringComparison.OrdinalIgnoreCase));
        var result = new JsonObject { ["build"] = Build };
        if (RecipesBuild is not null && RecipesBuild != Build)
            result["recipesVerifiedOn"] = RecipesBuild;
        if (words.Length == 0)
        {
            result["recipes"] = new JsonArray([.. RecipeNames.Select(n => JsonValue.Create(n))]);
            result["dialogs"] = new JsonArray([.. Items("dialogs").Select(d => (string?)d["title"]).Where(t => !string.IsNullOrEmpty(t))
                .Distinct().Select(t => JsonValue.Create(t))]);
            return result;
        }
        var recipes = new JsonObject();
        foreach (var (name, recipe) in _recipes ?? [])
            if (recipes.Count < limit && (words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)) || Hit(recipe)))
                recipes[name] = recipe!.DeepClone();
        Add(result, "recipes", recipes);
        Add(result, "menus", Matches(Items("menus"), Hit, limit));
        var commands = new JsonObject();
        foreach (var (id, text) in _doc["commands"] as JsonObject ?? [])
            if (commands.Count < limit && Hit(text))
                commands[id] = text!.DeepClone();
        Add(result, "commands", commands);
        Add(result, "accelerators", Matches(Items("accelerators"), Hit, limit));
        Add(result, "dialogs", Matches(Items("dialogs"), Hit, Math.Min(limit, 10)));
        return result;
    }

    private IEnumerable<JsonNode> Items(string key) => (_doc[key] as JsonArray ?? []).Where(n => n is not null)!;

    private static JsonArray Matches(IEnumerable<JsonNode> items, Func<JsonNode?, bool> hit, int limit) =>
        [.. items.Where(hit).Take(limit).Select(n => n.DeepClone())];

    private static void Add(JsonObject result, string key, JsonNode node)
    {
        if (node is JsonObject { Count: > 0 } or JsonArray { Count: > 0 })
            result[key] = node;
    }

    /// <summary>The whole file as JSON text (for tests and tools).</summary>
    public string ToJson() => _doc.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
