using System.Text;

namespace HammerMcp.Core.Fgd;

/// <summary>
/// Tolerant FGD parser: Valve Source / GoldSrc syntax plus Hammer++ (@version, @ExtendClass) and GMod (*type) extensions.
/// Unknown @commands and broken class bodies are skipped with a warning instead of failing the whole file.
/// </summary>
public sealed class FgdParser
{
    private static readonly HashSet<string> IgnoredCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "MaterialExclusion", "AutoVisGroup", "VisGroupFilter", "GridNav", "EntityGroup", "Exclude", "Main",
        // Source 2 model / data definitions (not entity classes)
        "ModelGameData", "ModelAnimEvent", "ModelBreakCommand", "VData", "Struct", "helpinfo",
    };

    private readonly Func<string, string?> _includeResolver;
    private readonly HashSet<string> _included = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="includeResolver">Maps an @include name to a full path (null if not found). Default: next to the including file.</param>
    public FgdParser(Func<string, string?>? includeResolver = null) => _includeResolver = includeResolver ?? (_ => null);

    public static FgdDocument ParseFile(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        // Source 2: game/<mod>/x.fgd includes files from game/core/
        var core = Path.Combine(dir, "..", "core");
        var parser = new FgdParser(name => FindNear(dir, name) ?? FindNear(Path.GetDirectoryName(Path.Combine(core, name)) ?? core, Path.GetFileName(name)));
        return parser.Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    public FgdDocument Parse(string text, string fileName = "<fgd>")
    {
        var doc = new FgdDocument();
        ParseInto(doc, text, fileName);
        return doc;
    }

    private void ParseInto(FgdDocument doc, string text, string fileName)
    {
        var t = new Tokens(Tokenizer.Tokenize(text), fileName, doc.Warnings);
        while (!t.End)
        {
            if (!t.Is("@"))
            {
                t.Warn($"unexpected '{t.Peek.Text}' at top level");
                t.SkipToNextCommand();
                continue;
            }
            t.Next();
            var command = t.Next().Text;
            try
            {
                if (command.Equals("include", StringComparison.OrdinalIgnoreCase))
                    Include(doc, t.Next().Text, t);
                else if (command.Equals("mapsize", StringComparison.OrdinalIgnoreCase))
                    doc.MapSize = MapSize(t);
                else if (command.Equals("version", StringComparison.OrdinalIgnoreCase))
                    doc.Version = int.TryParse(Parenthesized(t).Trim(), out var v) ? v : null;
                else if (command.EndsWith("Class", StringComparison.OrdinalIgnoreCase))
                {
                    var cls = Class(command, t);
                    cls.SourceFile = fileName;
                    doc.Add(cls);
                }
                else
                {
                    if (!IgnoredCommands.Contains(command))
                        t.Warn($"unknown command @{command} skipped");
                    t.SkipToNextCommand();
                }
            }
            catch (FgdSyntaxException e)
            {
                t.Warn(e.Message);
                t.SkipToNextCommand();
            }
        }
    }

    private void Include(FgdDocument doc, string name, Tokens t)
    {
        doc.Includes.Add(name);
        var path = _includeResolver(name);
        if (path is null)
        {
            t.Warn($"@include \"{name}\" not found");
            return;
        }
        if (!_included.Add(Path.GetFullPath(path)))
            return;
        ParseInto(doc, File.ReadAllText(path), Path.GetFileName(path));
    }

    private static (int, int)? MapSize(Tokens t)
    {
        var parts = Parenthesized(t).Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi) ? (lo, hi) : null;
    }

    private static FgdClass Class(string command, Tokens t)
    {
        var bases = new List<string>();
        var behaviours = new List<FgdBehaviour>();
        Dictionary<string, string>? metadata = null;
        while (!t.End && !t.Is("="))
        {
            if (t.Is("{"))
            {
                // Source 2: editor metadata before '=': { entity_tool_name = "..." entity_tool_group = "..." }
                metadata = Metadata(t);
                continue;
            }
            var name = t.Next();
            if (name.Kind != TokenKind.Word)
                throw new FgdSyntaxException($"expected helper name, got '{name.Text}'");
            var args = t.Is("(") ? Parenthesized(t) : "";
            if (name.Text.Equals("base", StringComparison.OrdinalIgnoreCase))
                bases.AddRange(args.Split([',', ' ', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            else
                behaviours.Add(new FgdBehaviour(name.Text, args.Trim()));
        }
        t.Expect("=");
        var cls = new FgdClass { Name = t.Next().Text, Kind = command };
        cls.Bases.AddRange(bases);
        cls.Behaviours.AddRange(behaviours);
        if (metadata is not null)
            foreach (var kv in metadata)
                cls.Metadata[kv.Key] = kv.Value;
        if (t.Is(":"))
        {
            t.Next();
            cls.Description = t.Peek.Kind == TokenKind.String ? t.Next().Text : null;
        }
        while (t.Is(":")) // GoldSrc FGDs sometimes add a help URL: = name : "desc" : "http://..."
        {
            t.Next();
            if (t.Peek.Kind == TokenKind.String)
                t.Next();
        }
        // H++ allows ExtendClass headers like "@ExtendClass base(X) = name [" and some FGDs omit the body
        if (!t.Is("["))
            return cls;
        t.Next();
        while (!t.End && !t.Is("]"))
        {
            try
            {
                Member(cls, t);
            }
            catch (FgdSyntaxException e)
            {
                t.Warn($"{cls.Name}: {e.Message}");
                t.SkipMember();
            }
        }
        t.Expect("]");
        return cls;
    }

    private static void Member(FgdClass cls, Tokens t)
    {
        if (t.Is("@"))
        {
            // nested blocks like @resources [ ... ] (CS:GO / Strata)
            t.Next();
            t.Next();
            if (t.Is("["))
                t.SkipBlock();
            return;
        }
        var first = t.Next();
        if (first.Kind != TokenKind.Word)
            throw new FgdSyntaxException($"expected keyvalue name, got '{first.Text}'");

        if ((first.Text.Equals("input", StringComparison.OrdinalIgnoreCase) || first.Text.Equals("output", StringComparison.OrdinalIgnoreCase))
            && t.Peek.Kind == TokenKind.Word && t.PeekAt(1).Text == "(")
        {
            var name = t.Next().Text;
            var type = Parenthesized(t).Trim().ToLowerInvariant();
            SkipPropertyMetadata(t);
            string? desc = null;
            if (t.Is(":"))
            {
                t.Next();
                desc = OptionalValue(t);
            }
            (first.Text.Equals("input", StringComparison.OrdinalIgnoreCase) ? cls.Inputs : cls.Outputs).Add(new FgdIo(name, type, desc));
            return;
        }

        if (!t.Is("("))
            throw new FgdSyntaxException($"expected '(' after keyvalue '{first.Text}'");
        var prop = new FgdProperty { Name = first.Text, Type = Parenthesized(t).Trim().TrimStart('*').ToLowerInvariant() };
        SkipPropertyMetadata(t);
        while (IsFlagWord(t))
        {
            var flag = t.Next().Text;
            prop.ReadOnly |= flag.Equals("readonly", StringComparison.OrdinalIgnoreCase);
            prop.Report |= flag.Equals("report", StringComparison.OrdinalIgnoreCase);
        }
        var fields = new List<string?>();
        while (t.Is(":"))
        {
            t.Next();
            fields.Add(OptionalValue(t));
        }
        prop.DisplayName = fields.ElementAtOrDefault(0);
        prop.DefaultValue = fields.ElementAtOrDefault(1);
        prop.Description = fields.ElementAtOrDefault(2);
        if (t.Is("="))
        {
            t.Next();
            Options(prop, t);
        }
        cls.Properties.Add(prop);
    }

    /// <summary>Source 2: per-member metadata right after the type, "[ group = "x" ]" or "{ group="x" enabled={...} }".</summary>
    private static void SkipPropertyMetadata(Tokens t)
    {
        while (t.Is("[") || t.Is("{"))
            t.SkipBalanced();
    }

    /// <summary>Top-level key = value pairs of a { ... } block; nested blocks are skipped.</summary>
    private static Dictionary<string, string> Metadata(Tokens t)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        t.Expect("{");
        while (!t.End && !t.Is("}"))
        {
            var key = t.Next();
            if (!t.Is("="))
                continue;
            t.Next();
            if (t.Is("{") || t.Is("["))
                t.SkipBalanced();
            else
                result[key.Text] = t.Next().Text;
        }
        if (t.Is("}"))
            t.Next();
        return result;
    }

    private static bool IsFlagWord(Tokens t) =>
        t.Peek.Kind == TokenKind.Word && t.PeekAt(1).Text != "("
        && (t.Peek.Text.Equals("readonly", StringComparison.OrdinalIgnoreCase) || t.Peek.Text.Equals("report", StringComparison.OrdinalIgnoreCase));

    /// <summary>A value after ':' - string, number or word; empty if the next token starts something else.</summary>
    private static string? OptionalValue(Tokens t)
    {
        var p = t.Peek;
        if (p.Kind == TokenKind.String || p.Kind == TokenKind.Number)
            return t.Next().Text;
        if (p.Kind == TokenKind.Word && t.PeekAt(1).Text != "(" && !(p.Text is "input" or "output" && t.PeekAt(2).Text == "("))
            return t.Next().Text;
        return null;
    }

    private static void Options(FgdProperty prop, Tokens t)
    {
        t.Expect("[");
        while (!t.End && !t.Is("]"))
        {
            var value = t.Next();
            if (value.Kind == TokenKind.Symbol)
                throw new FgdSyntaxException($"{prop.Name}: bad option '{value.Text}'");
            string label = "";
            bool? on = null;
            if (t.Is(":"))
            {
                t.Next();
                label = OptionalValue(t) ?? "";
            }
            if (t.Is(":"))
            {
                t.Next();
                on = OptionalValue(t) is { } d ? d == "1" : null;
            }
            // some FGDs add a 4th field (description) to flags
            while (t.Is(":"))
            {
                t.Next();
                OptionalValue(t);
            }
            prop.Options.Add(new FgdOption(value.Text, label, on));
        }
        t.Expect("]");
    }

    /// <summary>Raw text between balanced parentheses (strings re-quoted).</summary>
    private static string Parenthesized(Tokens t)
    {
        t.Expect("(");
        var sb = new StringBuilder();
        var depth = 1;
        while (!t.End)
        {
            var tok = t.Next();
            if (tok.Text == "(" && tok.Kind == TokenKind.Symbol)
                depth++;
            else if (tok.Text == ")" && tok.Kind == TokenKind.Symbol && --depth == 0)
                return sb.ToString();
            if (sb.Length > 0 && tok.Text is not "," && sb[^1] is not '(')
                sb.Append(' ');
            sb.Append(tok.Kind == TokenKind.String ? $"\"{tok.Text}\"" : tok.Text);
        }
        throw new FgdSyntaxException("unterminated '('");
    }

    private static string? FindNear(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        if (File.Exists(path))
            return path;
        // case-insensitive lookup (FGDs written on Windows, read on any OS)
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(name), StringComparison.OrdinalIgnoreCase))
            : null;
    }

    // ---- tokens ----------------------------------------------------------------------------------------------

    internal enum TokenKind { Word, String, Number, Symbol }

    internal readonly record struct Token(TokenKind Kind, string Text, int Line);

    internal static class Tokenizer
    {
        private const string Symbols = "@()[]{}=:,";

        /// <summary>
        /// A '"' preceded by a backslash is an escaped quote (Source 2 FGDs: "... \"Support multiple ...\" ...") only when
        /// another quote follows on the same line; otherwise the backslash ends a path ("C:\dir\") and the quote closes it.
        /// </summary>
        private static bool IsEscapedQuote(string text, int i)
        {
            if (i <= 0 || i >= text.Length || text[i] != '"' || text[i - 1] != '\\')
                return false;
            var end = text.IndexOf('\n', i + 1);
            var rest = text.AsSpan(i + 1, (end < 0 ? text.Length : end) - i - 1);
            return rest.Contains('"');
        }

        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var line = 1;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\n')
                {
                    line++;
                    i++;
                }
                else if (char.IsWhiteSpace(c) || c == '+')
                    i++; // a '+' not followed by a string (dangling concatenation) is ignored, as Hammer does
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                        i++;
                }
                else if (c == '"')
                {
                    var sb = new StringBuilder();
                    var startLine = line;
                    while (true)
                    {
                        i++;
                        while (i < text.Length && (text[i] != '"' || IsEscapedQuote(text, i)))
                        {
                            if (text[i] == '\n')
                                line++;
                            if (text[i] == '\\' && IsEscapedQuote(text, i + 1))
                            {
                                i++; // \" inside a string (Source 2 FGDs): keep the quote
                                continue;
                            }
                            sb.Append(text[i]);
                            i++;
                        }
                        i++; // closing quote
                        // "a" + "b" concatenation, possibly across lines
                        var j = i;
                        while (j < text.Length && char.IsWhiteSpace(text[j]))
                            j++;
                        if (j < text.Length && text[j] == '+')
                        {
                            var k = j + 1;
                            while (k < text.Length && char.IsWhiteSpace(text[k]))
                                k++;
                            if (k < text.Length && text[k] == '"')
                            {
                                line += text.AsSpan(i, k - i).Count('\n');
                                i = k;
                                continue;
                            }
                        }
                        break;
                    }
                    tokens.Add(new Token(TokenKind.String, sb.ToString(), startLine));
                }
                else if (Symbols.Contains(c))
                {
                    tokens.Add(new Token(TokenKind.Symbol, c.ToString(), line));
                    i++;
                }
                else
                {
                    var start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && !Symbols.Contains(text[i]) && text[i] != '"')
                        i++;
                    var word = text[start..i];
                    var kind = double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                        ? TokenKind.Number
                        : TokenKind.Word;
                    tokens.Add(new Token(kind, word, line));
                }
            }
            return tokens;
        }
    }

    private sealed class Tokens(List<Token> items, string fileName, List<string> warnings)
    {
        private static readonly Token EndToken = new(TokenKind.Symbol, "<eof>", -1);
        private int _pos;

        public bool End => _pos >= items.Count;
        public Token Peek => PeekAt(0);
        public Token PeekAt(int offset) => _pos + offset < items.Count ? items[_pos + offset] : EndToken;
        public bool Is(string symbol) => !End && items[_pos].Kind == TokenKind.Symbol && items[_pos].Text == symbol;

        public Token Next()
        {
            if (End)
                throw new FgdSyntaxException("unexpected end of file");
            return items[_pos++];
        }

        public void Expect(string symbol)
        {
            if (!Is(symbol))
                throw new FgdSyntaxException($"expected '{symbol}', got '{Peek.Text}'");
            _pos++;
        }

        public void Warn(string message) => warnings.Add($"{fileName}:{(End ? items.LastOrDefault().Line : Peek.Line)}: {message}");

        /// <summary>Skips to the next '@' that is not inside [ ] (start of the next top-level command).</summary>
        public void SkipToNextCommand()
        {
            var depth = 0;
            while (!End)
            {
                var tok = items[_pos];
                if (tok.Kind == TokenKind.Symbol)
                {
                    if (tok.Text == "[")
                        depth++;
                    else if (tok.Text == "]")
                        depth = Math.Max(0, depth - 1);
                    else if (tok.Text == "@" && depth == 0)
                        return;
                }
                _pos++;
            }
        }

        /// <summary>Inside a class body: skips the broken member up to the start of the next line's member or the class end.</summary>
        public void SkipMember()
        {
            var line = End ? -1 : items[_pos].Line;
            var depth = 0;
            while (!End)
            {
                var tok = items[_pos];
                if (tok.Kind == TokenKind.Symbol && tok.Text == "[")
                    depth++;
                else if (tok.Kind == TokenKind.Symbol && tok.Text == "]")
                {
                    if (depth == 0)
                        return; // end of class body
                    depth--;
                }
                else if (depth == 0 && tok.Line != line && tok.Kind == TokenKind.Word)
                    return;
                _pos++;
            }
        }

        /// <summary>Skips a "[...]" or "{...}" block, nesting of both kinds included.</summary>
        public void SkipBalanced()
        {
            var depth = 0;
            do
            {
                var tok = Next();
                if (tok.Kind == TokenKind.Symbol && tok.Text is "[" or "{")
                    depth++;
                else if (tok.Kind == TokenKind.Symbol && tok.Text is "]" or "}")
                    depth--;
            }
            while (!End && depth > 0);
        }

        public void SkipBlock()
        {
            Expect("[");
            var depth = 1;
            while (!End && depth > 0)
            {
                var tok = Next();
                if (tok.Kind == TokenKind.Symbol && tok.Text == "[")
                    depth++;
                else if (tok.Kind == TokenKind.Symbol && tok.Text == "]")
                    depth--;
            }
        }
    }
}

public sealed class FgdSyntaxException(string message) : Exception(message);
