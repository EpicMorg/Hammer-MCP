using System.Text;
using ValveKeyValue;

namespace HammerMcp.Core.Compile;

/// <summary>What a compile command does (Hammer's "special" codes; 0 = run an executable).</summary>
public enum CompileCommandKind
{
    Run = 0,
    ChangeDirectory = 256,
    CopyFile = 257,
    DeleteFile = 258,
    RenameFile = 259,
}

/// <summary>One step of a Run Map sequence.</summary>
public sealed record CompileCommand(
    bool Enabled,
    int Special,
    string Run,
    string Parameters,
    bool EnsureCheck,
    string? EnsureFile,
    bool UseProcessWindow,
    bool NoWait)
{
    public CompileCommandKind Kind => Enum.IsDefined(typeof(CompileCommandKind), Special) ? (CompileCommandKind)Special : CompileCommandKind.Run;
}

public sealed record CompileSequence(string Name, IReadOnlyList<CompileCommand> Commands);

/// <summary>
/// Hammer's Run Map sequences: binary CmdSeq.wc (Worldcraft / Hammer 3.x / 4.x, version 0.2: 804-byte commands),
/// text hammerplusplus_sequences.cfg (Hammer++: enable / specialcmd / run / parms) and Strata's cmdseq.wc
/// (KeyValues: enabled / special_cmd / run / params / ensure_check).
/// </summary>
public static class CompileSequences
{
    private const string BinaryHeader = "Worldcraft Command Sequences\r\n\u001a";

    /// <summary>Sequence files of a Hammer: Hammer++ cfg, stock CmdSeq.wc next to the exe (or one level up from bin/x64).</summary>
    public static IEnumerable<string> CandidateFiles(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath) ?? ".";
        yield return Path.Combine(dir, "hammerplusplus", "hammerplusplus_sequences.cfg");
        yield return Path.Combine(dir, "CmdSeq.wc");
        yield return Path.Combine(dir, "..", "CmdSeq.wc");
        // Strata: <game>\bin\win64\hammer.exe, sequences in <game>\hammer\cfg\cmdseq.wc
        yield return Path.Combine(dir, "..", "..", "hammer", "cfg", "cmdseq.wc");
    }

    public static (string? File, List<CompileSequence> Sequences) ForExe(string exePath)
    {
        foreach (var file in CandidateFiles(exePath))
        {
            if (File.Exists(file))
                return (Path.GetFullPath(file), Load(file));
        }
        return (null, []);
    }

    public static List<CompileSequence> Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.AsSpan().StartsWith(Encoding.ASCII.GetBytes(BinaryHeader)) ? ParseBinary(bytes) : ParseText(bytes);
    }

    public static List<CompileSequence> ParseBinary(byte[] d)
    {
        var offset = BinaryHeader.Length + 4; // header + float version (0.2)
        var count = BitConverter.ToInt32(d, offset);
        offset += 4;
        var result = new List<CompileSequence>();
        for (var s = 0; s < count && offset + 132 <= d.Length; s++)
        {
            var name = CString(d, offset, 128);
            var commandCount = BitConverter.ToInt32(d, offset + 128);
            offset += 132;
            var commands = new List<CompileCommand>();
            for (var c = 0; c < commandCount && offset + 804 <= d.Length; c++, offset += 804)
            {
                commands.Add(new CompileCommand(
                    Enabled: BitConverter.ToInt32(d, offset) != 0,
                    Special: BitConverter.ToInt32(d, offset + 4),
                    Run: CString(d, offset + 8, 260),
                    Parameters: CString(d, offset + 268, 260),
                    EnsureCheck: BitConverter.ToInt32(d, offset + 528) != 0,
                    EnsureFile: CString(d, offset + 532, 260) is { Length: > 0 } f ? f : null,
                    UseProcessWindow: BitConverter.ToInt32(d, offset + 796) != 0,
                    NoWait: BitConverter.ToInt32(d, offset + 800) != 0));
            }
            result.Add(new CompileSequence(name, commands));
        }
        return result;
    }

    public static List<CompileSequence> ParseText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var doc = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        var result = new List<CompileSequence>();
        foreach (var (name, seq) in doc.Root.Children)
        {
            if (!seq.IsCollection)
                continue;
            var commands = new List<CompileCommand>();
            foreach (var (_, cmd) in seq.Children.OrderBy(c => int.TryParse(c.Key, out var n) ? n : int.MaxValue))
            {
                if (!cmd.IsCollection)
                    continue;
                // Strata writes escaped backslashes ("$path\\$file"), Hammer++ plain ones ("$path\$file")
                string Get(params string[] keys) =>
                    (keys.Select(k => cmd.Children.FirstOrDefault(c => c.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Value)
                        .FirstOrDefault(v => v is not null && !v.IsCollection)?.ToString() ?? "").Replace(@"\\", @"\");
                commands.Add(new CompileCommand(
                    Enabled: Get("enable", "enabled") != "0",
                    Special: int.TryParse(Get("specialcmd", "special_cmd"), out var sp) ? sp : 0,
                    Run: Get("run"),
                    Parameters: Get("parms", "params"),
                    EnsureCheck: Get("ensure_check", "ensurecheck") == "1",
                    EnsureFile: Get("ensure_fn", "ensurefile") is { Length: > 0 } f ? f : null,
                    UseProcessWindow: Get("use_process_wnd", "useprocwnd") != "0",
                    NoWait: Get("no_wait", "nowait") == "1"));
            }
            result.Add(new CompileSequence(name, commands));
        }
        return result;
    }

    /// <summary>
    /// Expands Hammer's Run Map variables: $bsp_exe $vis_exe $light_exe $game_exe $gamedir $gamename $bspdir $exedir
    /// $path (map folder) $file (map name) $ext (map extension); unknown variables are left as is.
    /// </summary>
    public static string Expand(string text, IReadOnlyDictionary<string, string> variables)
    {
        var sb = new StringBuilder(text);
        foreach (var (key, value) in variables.OrderByDescending(v => v.Key.Length))
            sb.Replace("$" + key, value);
        return sb.ToString();
    }

    /// <summary>
    /// Expands a command-line template token by token and quotes every token that ends up containing spaces
    /// ("-game $gamedir $path\$file" with "C:\Steam Library\..." stays three arguments), like Hammer's Run Map.
    /// Tokens already in quotes are kept as they are.
    /// </summary>
    public static string ExpandArguments(string template, IReadOnlyDictionary<string, string> variables)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in template)
        {
            if (ch == '"')
                quoted = !quoted;
            if (ch == ' ' && !quoted)
            {
                if (current.Length > 0)
                    tokens.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(ch);
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return string.Join(' ', tokens.Select(t =>
        {
            var expanded = Expand(t, variables);
            return expanded.Contains(' ') && !expanded.StartsWith('"') ? $"\"{expanded}\"" : expanded;
        }));
    }

    private static string CString(byte[] d, int offset, int length)
    {
        var end = Array.IndexOf(d, (byte)0, offset, length);
        return Encoding.Latin1.GetString(d, offset, (end < 0 ? offset + length : end) - offset);
    }
}
