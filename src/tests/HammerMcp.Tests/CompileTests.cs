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
