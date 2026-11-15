using System.Text;

namespace HammerMcp.Core.Assets;

/// <summary>
/// Source 1 sound scripts: named sounds ("Ambient.Wind") from the files that scripts/game_sounds_manifest.txt lists
/// (precache_file, preload_file, ...), read from loose folders or VPKs. The first definition of a name wins, as in the
/// engine's sound emitter. Source 2 (vsndevts) is a different format and is not read here.
/// </summary>
public sealed class SoundScripts
{
    public const string ManifestPath = "scripts/game_sounds_manifest.txt";

    /// <summary>A named sound: the script that defines it (relative path), the folder / VPK it came from, its channel and waves as written.</summary>
    public sealed record Entry(string Name, string Script, string Source, string? Channel, IReadOnlyList<string> Waves);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<Entry> Entries => _entries.Values;
    /// <summary>Script files read, in manifest order.</summary>
    public List<string> Scripts { get; } = [];
    public List<string> Problems { get; } = [];
    /// <summary>Names defined again in a later script (ignored, the first one wins).</summary>
    public int Duplicates { get; private set; }

    public bool Contains(string name) => _entries.ContainsKey(name);
    public Entry? Find(string name) => _entries.GetValueOrDefault(name);

    public static SoundScripts Load(AssetResolver assets)
    {
        var result = new SoundScripts();
        List<string> files;
        if (assets.Read(ManifestPath) is { } manifest)
            files = ManifestFiles(Encoding.UTF8.GetString(manifest.Data));
        else
        {
            // no manifest (some mods ship only the scripts): every scripts/game_sounds*.txt, by name
            result.Problems.Add($"{ManifestPath} not found: reading scripts/game_sounds*.txt");
            files = assets.List("scripts")
                .Select(f => f.Path)
                .Where(p => Path.GetFileName(p).StartsWith("game_sounds", StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                            && !p.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        foreach (var file in files)
        {
            if (!result.AddFile(assets, file))
                result.Problems.Add($"sound script not found: {file}");
        }
        return result;
    }

    /// <summary>
    /// The map's own sound script, maps/&lt;map&gt;_level_sounds.txt, which the engine loads with the map; null if the game
    /// has none. Its names come on top of the game's sound scripts.
    /// </summary>
    public static SoundScripts? LoadLevel(AssetResolver assets, string mapName)
    {
        var level = new SoundScripts();
        return level.AddFile(assets, $"maps/{mapName}_level_sounds.txt") ? level : null;
    }

    private bool AddFile(AssetResolver assets, string file)
    {
        if (assets.Read(file) is not { } script)
            return false;
        Scripts.Add(file);
        Add(file, script.Source, Parse(Encoding.UTF8.GetString(script.Data), file, assets, 0, Problems));
        return true;
    }

    private void Add(string script, string source, IEnumerable<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children is null)
                continue;
            if (_entries.ContainsKey(node.Key))
            {
                Duplicates++;
                continue;
            }
            var channel = node.Children.FirstOrDefault(c => c.Key.Equals("channel", StringComparison.OrdinalIgnoreCase))?.Value;
            var waves = new List<string>();
            foreach (var c in node.Children)
            {
                if (c.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) && c.Value is { Length: > 0 } w)
                    waves.Add(w);
                else if (c.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase) && c.Children is { } rnd)
                    waves.AddRange(rnd.Where(r => r.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) && r.Value is { Length: > 0 }).Select(r => r.Value!));
            }
            _entries[node.Key] = new Entry(node.Key, script, source, channel, waves);
        }
    }

    /// <summary>Scripts the manifest lists: every "*_file" value (precache_file, preload_file, autocache_file, ...) in order.</summary>
    public static List<string> ManifestFiles(string manifest)
    {
        var root = Parse(manifest, ManifestPath, null, 0, []);
        var block = root.FirstOrDefault(n => n.Children is not null)?.Children ?? [];
        return block.Where(n => n.Key.EndsWith("_file", StringComparison.OrdinalIgnoreCase) && n.Value is { Length: > 0 })
            .Select(n => n.Value!.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The sound file a wave names: sound chars (*stream, #drymix, @omni, )spatial, ^distvariant, &lt;&gt; direction, ...) stripped,
    /// under sound/. Null for a sentence (!name) or an empty wave.
    /// </summary>
    public static string? WaveFile(string wave)
    {
        var w = wave.Trim();
        var i = 0;
        while (i < w.Length && IsSoundChar(w[i]))
        {
            if (w[i] == '!')
                return null;
            i++;
        }
        var path = w[i..].Replace('\\', '/').TrimStart('/');
        return path.Length == 0 ? null : "sound/" + path;
    }

    /// <summary>soundchars.h: * stream, ? uservox, ! sentence, # drymix, &gt; doppler, &lt; directional, ^ distvariant,
    /// @ omni, ) spatial stereo, ( directional stereo, } fast pitch, $ HRTF blend, ~ HRTF force, &amp; HRTF, + radio.</summary>
    public static bool IsSoundChar(char c) => c is '*' or '?' or '!' or '#' or '>' or '<' or '^' or '@' or ')' or '(' or '}' or '$' or '~' or '&' or '+';

    // --- KeyValues 1 text, tolerant: several root keys, // comments, [$CONDITION] tags skipped, #base / #include ---

    private sealed record Node(string Key, string? Value, List<Node>? Children);

    private static List<Node> Parse(string text, string file, AssetResolver? assets, int depth, List<string> problems)
    {
        var tokens = Tokenize(text);
        var pos = 0;
        var nodes = ParseBlock(tokens, ref pos, topLevel: true);
        if (assets is null)
            return nodes;
        var result = new List<Node>();
        foreach (var n in nodes)
        {
            if (n.Key is "#base" or "#include" && n.Value is { Length: > 0 } inc)
            {
                // relative to the folder of the including file, as KeyValues::LoadFromFile does
                var dir = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? "";
                var path = (dir.Length > 0 ? dir + "/" : "") + inc.Replace('\\', '/');
                if (depth >= 8)
                    problems.Add($"{file}: {n.Key} nested too deep");
                else if (assets.Read(path) is { } data)
                    result.AddRange(Parse(Encoding.UTF8.GetString(data.Data), path, assets, depth + 1, problems));
                else
                    problems.Add($"{file}: {n.Key} \"{inc}\" not found");
                continue;
            }
            result.Add(n);
        }
        return result;
    }

    private static List<Node> ParseBlock(List<string?> tokens, ref int pos, bool topLevel)
    {
        var nodes = new List<Node>();
        while (pos < tokens.Count)
        {
            var key = tokens[pos++];
            if (key is null) // '}'
            {
                if (topLevel)
                    continue; // stray brace
                return nodes;
            }
            if (key == "{")
            {
                ParseBlock(tokens, ref pos, false); // a block with no key: skip it
                continue;
            }
            if (pos >= tokens.Count)
                break;
            var value = tokens[pos];
            if (value == "{")
            {
                pos++;
                nodes.Add(new Node(key, null, ParseBlock(tokens, ref pos, false)));
            }
            else if (value is not null)
            {
                pos++;
                nodes.Add(new Node(key, value, null));
            }
        }
        return nodes;
    }

    /// <summary>Strings and bare words; "{" for an opening brace, null for a closing one. Comments and [$X] tags are dropped.</summary>
    private static List<string?> Tokenize(string text)
    {
        var tokens = new List<string?>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == '﻿')
                i++;
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
            }
            else if (c == '{')
            {
                tokens.Add("{");
                i++;
            }
            else if (c == '}')
            {
                tokens.Add(null);
                i++;
            }
            else if (c == '[')
            {
                var end = text.IndexOf(']', i);
                i = end < 0 ? text.Length : end + 1;
            }
            else if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0)
                    end = text.Length;
                tokens.Add(text[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                    i++;
                tokens.Add(text[start..i]);
            }
        }
        return tokens;
    }
}
