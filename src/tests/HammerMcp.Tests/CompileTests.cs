using System.Text;
using HammerMcp.Core.Compile;

namespace HammerMcp.Tests;

public class CompileTests
{
    private static byte[] BinaryCmdSeq()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("Worldcraft Command Sequences\r\n\u001a"));
        w.Write(0.2f);
        w.Write(1); // sequences
        void Fixed(string s, int len) { var b = new byte[len]; Encoding.ASCII.GetBytes(s).CopyTo(b, 0); w.Write(b); }
        Fixed("Default", 128);
        w.Write(2); // commands
        void Command(int enable, int special, string run, string parms, int useWnd, int noWait)
        {
            w.Write(enable); w.Write(special); Fixed(run, 260); Fixed(parms, 260);
            w.Write(1); Fixed("", 260); w.Write(0); w.Write(useWnd); w.Write(noWait);
        }
        Command(1, 0, "$bsp_exe", "-game $gamedir $path\\$file", 1, 0);
        Command(1, 257, "Copy File", "$path\\$file.bsp $bspdir\\$file.bsp", 1, 0);
        return ms.ToArray();
    }

    [Fact]
    public void ReadsBinaryCmdSeq()
    {
        var seq = CompileSequences.ParseBinary(BinaryCmdSeq()).Single();
        Assert.Equal("Default", seq.Name);
        Assert.Equal(CompileCommandKind.Run, seq.Commands[0].Kind);
        Assert.Equal("$bsp_exe", seq.Commands[0].Run);
        Assert.Equal(CompileCommandKind.CopyFile, seq.Commands[1].Kind);
        Assert.True(seq.Commands[1].EnsureCheck);
    }

    [Theory]
    [InlineData("\"Command Sequences\" { \"Fast\" { \"0\" { \"enable\" \"1\" \"specialcmd\" \"0\" \"run\" \"$bsp_exe\" \"parms\" \"-game $gamedir $path\\$file\" } } }")]
    [InlineData("\"Command Sequences\" { \"Fast\" { \"1\" { \"enabled\" \"1\" \"special_cmd\" \"0\" \"run\" \"$bsp_exe\" \"params\" \"-game $gamedir $path\\\\$file\" } } }")]
    public void ReadsHammerPlusPlusAndStrataText(string text)
    {
        var seq = CompileSequences.ParseText(Encoding.UTF8.GetBytes(text)).Single();
        Assert.Equal("Fast", seq.Name);
        Assert.Equal(@"-game $gamedir $path\$file", seq.Commands.Single().Parameters);
    }

    [Fact]
    public void ExpandsVariablesLongestFirst()
    {
        var vars = new Dictionary<string, string> { ["file"] = "m", ["path"] = @"C:\maps", ["bspdir"] = @"C:\g\maps", ["bsp_exe"] = "vbsp.exe" };
        Assert.Equal(@"vbsp.exe C:\maps\m C:\g\maps\m.bsp", CompileSequences.Expand(@"$bsp_exe $path\$file $bspdir\$file.bsp", vars));
    }

    [Fact]
    public void QuotesArgumentsWithSpacesLikeHammer()
    {
        var vars = new Dictionary<string, string> { ["gamedir"] = @"W:\Steam\common\Bestiarium Project\bst", ["path"] = @"W:\maps\my maps", ["file"] = "test" };
        Assert.Equal(@"-game ""W:\Steam\common\Bestiarium Project\bst"" ""W:\maps\my maps\test""",
            CompileSequences.ExpandArguments(@"-game $gamedir $path\$file", vars));
        Assert.Equal(@"-bounce 2 -game W:\g ""already quoted""",
            CompileSequences.ExpandArguments(@"-bounce 2 -game $gamedir ""already quoted""", new Dictionary<string, string> { ["gamedir"] = @"W:\g" }));
    }

    [Fact]
    public void ParsesCompileLog()
    {
        string[] lines =
        [
            "Valve Software - vbsp.exe (Sep 29 2014)",
            "0...1...2...3...4...5...6...7...8...9...10**** leaked ****",
            "Entity light_spot (512.00 -64.00 128.00) leaked!",
            "material \"metal/metalpipe001a\" not found.",
            "Material not found!: METAL/METALPIPE001A",
            "Error loading studio model \"models/props/x.mdl\"!",
            "WARNING: Too many light styles on a face at (1, 2, 3)",
            "Could not locate 'GameData' key in c:\\mod\\gameinfo.txt",
            "1 seconds elapsed",
            "Valve Software - vvis.exe (Sep 29 2014)",
            "2 seconds elapsed",
        ];
        var log = CompileLog.Parse(lines);
        Assert.True(log.Leaked);
        Assert.Equal(["light_spot"], log.LeakedEntities);
        Assert.Equal([("vbsp", 1.0), ("vvis", 2.0)], log.Stages.Select(s => (s.Name, s.Seconds ?? -1)));
        var material = log.Issues.Single(i => i.Kind == CompileIssueKind.MissingMaterial);
        Assert.Equal(("metal/metalpipe001a", 2), (material.Message, material.Count));
        Assert.Contains(log.Issues, i => i.Kind == CompileIssueKind.ModelError);
        Assert.Contains(log.Issues, i => i.Kind == CompileIssueKind.Limit);
        Assert.Contains(log.Issues, i => i.Kind == CompileIssueKind.Error && i.Message.Contains("GameData"));
    }

    [Fact]
    public void ParsesAGoldSrcCompileLog()
    {
        // VHLT hlbsp, a room with one wall missing
        string[] lines =
        [
            "hlcsg v3.4 VL33 (Feb  2 2014)",
            "-----  BEGIN  hlcsg -----",
            "0.02 seconds elapsed",
            "-----   END   hlcsg -----",
            "-----  BEGIN  hlbsp -----",
            "SolidBSP [hull 0] 20 (0.00 seconds)",
            "Warning: === LEAK in hull 0 ===",
            "Entity light @ (   0,   0, 100)",
            "Error: ",
            "  A LEAK is a hole in the map, where the inside of it is exposed to the",
            "indication of where the beginning of the leak pointfile starts, so the error is",
            "",
            "Leak pointfile generated",
            "Warning: === LEAK in hull 1 ===",
            "Entity light @ (   0,   0, 100)",
            "0.03 seconds elapsed",
        ];
        var log = CompileLog.Parse(lines);
        Assert.True(log.Leaked);
        Assert.Equal(["light"], log.LeakedEntities.Distinct());
        Assert.Equal([("hlcsg", 0.02), ("hlbsp", 0.03)], log.Stages.Select(s => (s.Name, s.Seconds ?? -1)));
        Assert.DoesNotContain(log.Issues, i => i.Kind == CompileIssueKind.Error);
        Assert.Equal(2, log.Issues.Count(i => i.Kind == CompileIssueKind.Leak && i.Message.StartsWith("map leaked (hull")));
    }

    [Fact]
    public void ParsesJackRunConfig()
    {
        // as Jackhammer 1.1 writes it: size before the last keys, a quoted title, other sections around
        string[] lines =
        [
            "[Config0]",
            "Title=\"Half-Life (Full, extra)\"",
            @"Commands\1\Type=2",
            @"Commands\1\Flags=1",
            @"Commands\1\Args=$gamedir",
            @"Commands\2\Type=3",
            @"Commands\2\Flags=1",
            @"Commands\2\Prog=$path/$file.$ext",
            @"Commands\2\Args=$bspdir/$file.$ext",
            @"Commands\3\Type=1",
            @"Commands\3\Flags=3",
            @"Commands\3\Prog=$csg_exe",
            @"Commands\3\Args=$bspdir/$file",
            @"Commands\3\FileCheck=$bspdir/$file.$bspext",
            @"Commands\4\Type=0",
            @"Commands\4\Flags=0",
            @"Commands\4\Prog=$game_exe",
            @"Commands\5\Type=5",
            @"Commands\5\Flags=1",
            @"Commands\5\Prog=C:/My Maps/a.bsp",
            @"Commands\size=5",
            @"Commands\4\Args=+map $file",
            @"Commands\5\Args=C:/b.bsp",
            @"Commands\9\Prog=ignored past size",
            "",
            "[General]",
            "x=1",
        ];
        var seq = JackRunCfg.Parse(lines).Single();
        Assert.Equal("Half-Life (Full, extra)", seq.Name);
        Assert.Equal(
            [CompileCommandKind.ChangeDirectory, CompileCommandKind.CopyFile, CompileCommandKind.Run, CompileCommandKind.Run, CompileCommandKind.RenameFile],
            seq.Commands.Select(c => c.Kind));
        Assert.Equal("$gamedir", seq.Commands[0].Parameters);
        Assert.Equal("$path/$file.$ext $bspdir/$file.$ext", seq.Commands[1].Parameters);
        Assert.Equal(("$csg_exe", "$bspdir/$file", true, "$bspdir/$file.$bspext"),
            (seq.Commands[2].Run, seq.Commands[2].Parameters, seq.Commands[2].EnsureCheck, seq.Commands[2].EnsureFile));
        Assert.False(seq.Commands[3].Enabled);
        Assert.Equal("+map $file", seq.Commands[3].Parameters);
        Assert.Equal("\"C:/My Maps/a.bsp\" C:/b.bsp", seq.Commands[4].Parameters);
        Assert.Equal("\"C:/My Maps/a.bsp\" C:/b.bsp", CompileSequences.ExpandArguments(seq.Commands[4].Parameters, new Dictionary<string, string>()));
    }

    [Fact]
    public void ReadsTheJackhammerRunConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hmcp-jack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "VDKRunCfg.ini"), ["[Config0]", "Title=Default", @"Commands\1\Type=1", @"Commands\1\Flags=1", @"Commands\1\Prog=$bsp_exe", @"Commands\size=1"]);
            var (file, sequences) = CompileSequences.ForExe(Path.Combine(dir, "jackhammer.exe"));
            Assert.EndsWith("VDKRunCfg.ini", file);
            Assert.Equal("$bsp_exe", sequences.Single().Commands.Single().Run);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReadsLeakTrace()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "512.000000 128.000000 80.000000\n75.325104 20.559601 73.000000\n");
        try
        {
            var trace = LeakTrace.Read(path);
            Assert.Equal(2, trace.Points.Count);
            Assert.InRange(trace.Length, 449, 451);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
