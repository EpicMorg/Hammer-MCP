using HammerMcp.Core.Config;

namespace HammerMcp.Core.Compile;

/// <summary>
/// VDKRunCfg.ini - Run Map profiles of J.A.C.K. and Jackhammer, next to jack.exe / jackhammer.exe (Qt QSettings ini):
/// [ConfigN] Title, Commands\size, Commands\I\Type / Flags / Prog / Args / FileCheck (I from 1).
/// Type: 0 executable, 1 compiler, 2 change directory (Args), 3 copy file, 4 delete file, 5 rename file (Prog -> Args);
/// Flags: bit 0 = enabled, bit 1 = check that FileCheck exists after the step.
/// </summary>
public static class JackRunCfg
{
    public static List<CompileSequence> Load(string path) => Parse(File.ReadAllLines(path));

    public static List<CompileSequence> Parse(IEnumerable<string> lines)
    {
        var result = new List<CompileSequence>();
        foreach (var (name, v) in JackGameCfg.Sections(lines).Where(s => s.Name.StartsWith("Config", StringComparison.OrdinalIgnoreCase)))
        {
            // QSettings arrays: Commands\size, Commands\1\..; the size line may come before or after the items
            var indices = v.Keys
                .Select(k => k.Split('\\'))
                .Where(p => p.Length == 3 && p[0].Equals("Commands", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[1], out _))
                .Select(p => int.Parse(p[1]))
                .Distinct()
                .Order();
            if (v.TryGetValue(@"Commands\size", out var sizeText) && int.TryParse(sizeText, out var size))
                indices = indices.Where(i => i <= size).Order();

            var commands = new List<CompileCommand>();
            foreach (var i in indices)
            {
                string Get(string key) => v.TryGetValue($@"Commands\{i}\{key}", out var s) ? s : "";
                var type = int.TryParse(Get("Type"), out var t) ? t : 1;
                var flags = int.TryParse(Get("Flags"), out var f) ? f : 1;
                var (prog, args) = (Get("Prog"), Get("Args"));
                var (special, run, parameters) = type switch
                {
                    2 => (CompileCommandKind.ChangeDirectory, "", args.Length > 0 ? args : prog),
                    3 => (CompileCommandKind.CopyFile, "", Join(prog, args)),
                    4 => (CompileCommandKind.DeleteFile, "", args.Length > 0 ? args : prog),
                    5 => (CompileCommandKind.RenameFile, "", Join(prog, args)),
                    _ => (CompileCommandKind.Run, prog, args),
                };
                commands.Add(new CompileCommand(
                    Enabled: (flags & 1) != 0,
                    Special: (int)special,
                    Run: run,
                    Parameters: parameters,
                    EnsureCheck: (flags & 2) != 0,
                    EnsureFile: Get("FileCheck") is { Length: > 0 } check ? check : null,
                    UseProcessWindow: true,
                    NoWait: false));
            }
            result.Add(new CompileSequence(v.TryGetValue("Title", out var title) && title.Length > 0 ? title : name, commands));
        }
        return result;
    }

    // copy / rename take "from to" in Parameters, like Hammer's CmdSeq.wc
    private static string Join(string from, string to) => $"{Quote(from)} {Quote(to)}".Trim();

    private static string Quote(string path) => path.Contains(' ') && !path.StartsWith('"') ? $"\"{path}\"" : path;
}
