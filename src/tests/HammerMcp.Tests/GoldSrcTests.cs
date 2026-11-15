using System.Text;
using HammerMcp.Core.Assets;
using HammerMcp.Core.Config;

namespace HammerMcp.Tests;

public class GoldSrcTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hmcp-goldsrc-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Str(BinaryWriter w, string s)
    {
        var b = new byte[128];
        Encoding.ASCII.GetBytes(s, b);
        w.Write(b);
    }

    /// <summary>GameCfg.wc as Hammer 3.x (1.4) or Worldcraft 1.6-2.x (1.3) writes it.</summary>
    private static byte[] GameCfg(float version, params (string Name, string[] Fields, string[] Fgds)[] configs)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("Game Configurations File\r\n\x1a\0"));
        w.Write(version);
        w.Write(configs.Length);
        foreach (var (name, fields, fgds) in configs)
        {
            Str(w, name);
            w.Write(fgds.Length);
            w.Write(2); // texture format
            w.Write(3); // map format
            foreach (var f in fields)
                Str(w, f);
            foreach (var f in fgds)
                Str(w, f);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ReadsHammer3Config()
    {
        // gameExe, solid, point, bsp, light, vis, gameExeDir, mapDir, bspDir, csg, modDir, baseDir
        var data = GameCfg(1.4f,
            ("Half-Life", [@"C:\HL\hl.exe", "func_wall", "info_player_start", @"C:\t\hlbsp.exe", @"C:\t\hlrad.exe", @"C:\t\hlvis.exe",
                @"C:\HL", @"C:\HL\mapsrc", @"C:\HL\valve\maps", @"C:\t\hlcsg.exe", @"C:\HL\valve", @"C:\HL\valve"], [@"fgd\halflife.fgd", @"C:\t\zhlt.fgd"]),
            ("Counter-Strike", ["", "", "", "", "", "", "", "", "", "", @"C:\HL\cstrike", @"C:\HL\valve"], []));
        var configs = GameCfgWc.Parse(new MemoryStream(data), @"C:\Editor");

        Assert.Equal(2, configs.Count);
        var hl = configs[0];
        Assert.True(hl.GoldSrc);
        Assert.Equal("Half-Life", hl.Name);
        Assert.Equal(@"C:\HL\valve", hl.GameDir);
        Assert.Equal(@"C:\HL\valve", hl.BaseGameDir);
        Assert.Equal(@"C:\HL", hl.GameExeDir);
        Assert.Equal(@"C:\HL\mapsrc", hl.MapDir);
        Assert.Equal(@"C:\HL\valve\maps", hl.BspDir);
        Assert.Equal([@"C:\Editor\fgd\halflife.fgd", @"C:\t\zhlt.fgd"], hl.FgdFiles);
        Assert.Equal(@"C:\t\hlcsg.exe", hl.Tools["CSG"]);
        Assert.Equal(@"C:\t\hlrad.exe", hl.Tools["Light"]);
        Assert.Equal(@"C:\HL\hl.exe", hl.Tools["GameExe"]);

        var cs = configs[1];
        Assert.Equal(@"C:\HL\cstrike", cs.GameDir);
        Assert.Equal(@"C:\HL\valve", cs.BaseGameDir);
        Assert.Null(cs.MapDir);
        Assert.Empty(cs.FgdFiles);
    }

    [Fact]
    public void ReadsWorldcraft16ConfigWithPalette()
    {
        // palette, gameExe, solid, point, bsp, light, vis, gameExeDir, mapDir, bspDir
        var data = GameCfg(1.3f,
            ("Quake", [@"C:\WC\quake.pal", @"C:\quake\quake.exe", "func_door", "info_player_start", @"C:\WC\qbsp.exe", @"C:\WC\light.exe",
                @"C:\WC\vis.exe", @"C:\quake", @"C:\quake\maps", @"C:\quake\id1\maps"], [@"C:\WC\quake.fgd"]));
        var quake = Assert.Single(GameCfgWc.Parse(new MemoryStream(data), @"C:\WC"));
        Assert.Equal(@"C:\quake\quake.exe", quake.Tools["GameExe"]);
        Assert.Equal(@"C:\WC\qbsp.exe", quake.Tools["BSP"]);
        Assert.Equal(@"C:\quake\id1\maps", quake.BspDir);
        Assert.Null(quake.GameDir);
        Assert.Equal([@"C:\WC\quake.fgd"], quake.FgdFiles);
    }

    [Fact]
    public void RejectsOtherFilesAndVersions()
    {
        Assert.Throws<InvalidDataException>(() => GameCfgWc.Parse(new MemoryStream(Encoding.ASCII.GetBytes("\"Configs\" { }")), "."));
        Assert.Throws<InvalidDataException>(() => GameCfgWc.Parse(new MemoryStream(GameCfg(2.0f)), "."));
        var truncated = GameCfg(1.4f, ("X", ["a"], []));
        Assert.Throws<InvalidDataException>(() => GameCfgWc.Parse(new MemoryStream(truncated), "."));
    }

    [Fact]
    public void ForExeFindsGameCfgWcNextToTheEditor()
    {
        File.WriteAllBytes(Path.Combine(_dir, "GameCfg.wc"), GameCfg(1.4f, ("Half-Life", new string[12], [@"fgd\halflife.fgd"])));
        var (file, configs) = HammerGameConfig.ForExe(Path.Combine(_dir, "hammer.exe"));
        Assert.Equal(Path.Combine(_dir, "GameCfg.wc"), file);
        Assert.Equal(Path.Combine(_dir, "fgd", "halflife.fgd"), Assert.Single(Assert.Single(configs).FgdFiles));
    }

    /// <summary>A WAD3 with the given lumps (type 0x43 = mip texture, 0x42 = qpic); names only.</summary>
    private string Wad(string path, params (string Name, byte Type)[] lumps)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("WAD3"));
        w.Write(lumps.Length);
        w.Write(12);
        foreach (var (name, type) in lumps)
        {
            w.Write(0); w.Write(0); w.Write(0);
            w.Write(type); w.Write((byte)0); w.Write((short)0);
            var b = new byte[16];
            Encoding.ASCII.GetBytes(name, b);
            w.Write(b);
        }
        File.WriteAllBytes(full, ms.ToArray());
        return full;
    }

    private void Files(params string[] files)
    {
        foreach (var f in files)
        {
            var path = Path.Combine(_dir, f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
    }

    [Fact]
    public void WadKeepsOnlyMipTextures()
    {
        var wad = WadDirectory.Open(Wad("a.wad", ("C1A0_LABW1", 0x43), ("conchars", 0x42), ("{GRATE", 0x43), ("c1a0_labw1", 0x43)));
        Assert.Equal(["C1A0_LABW1", "{GRATE"], wad.Textures);
        File.WriteAllText(Path.Combine(_dir, "b.wad"), "PACK");
        Assert.Throws<InvalidDataException>(() => WadDirectory.Open(Path.Combine(_dir, "b.wad")));
    }

    [Fact]
    public void SearchesModThenValveWithWads()
    {
        Files(@"valve\models\scientist.mdl", @"valve\models\scientistt.mdl", @"valve\models\scientist01.mdl", @"valve\models\w_9mmar.mdl",
            @"valve\sound\ambience\wind1.wav", @"valve\sprites\glow01.spr",
            @"mymod\models\scientist.mdl", @"mymod_downloads\sound\ambience\windy.wav", @"valve_addon\models\headcrab.mdl");
        Wad(@"valve\halflife.wad", ("c1a0_labflrd", 0x43), ("CRATE01", 0x43));
        Wad(@"valve\decals.wad", ("{scorch1", 0x43));
        Wad(@"valve\fonts.wad", ("font1", 0x43));
        Wad(@"mymod\mymod.wad", ("crate01", 0x43));
        var config = new HammerGameConfig("Mod", [], Path.Combine(_dir, "mymod"), _dir, null, null, new Dictionary<string, string>(),
            BaseGameDir: Path.Combine(_dir, "valve"), GoldSrc: true);

        Assert.Equal(
            [Path.Combine(_dir, "mymod"), Path.Combine(_dir, "mymod_downloads"), Path.Combine(_dir, "valve_addon"), Path.Combine(_dir, "valve")],
            GoldSrcContent.SearchFolders(config));
        var content = GoldSrcContent.ForConfig(config);
        Assert.Equal(["mymod.wad", "decals.wad", "halflife.wad"], content.Wads.Select(w => Path.GetFileName(w.Path)));

        // the mod's copy wins; texture and sequence-group files of a model are not offered
        var models = AssetSearch.SearchGoldSrc(content.Files, content.Wads, "scientist", "model", 50);
        var scientist = Assert.Single(models.Hits);
        Assert.Equal("models/scientist.mdl", scientist.KeyValue);
        Assert.Equal(Path.Combine(_dir, "mymod"), scientist.Sources[0]);
        Assert.Equal(2, scientist.Sources.Count);
        Assert.Contains(AssetSearch.SearchGoldSrc(content.Files, content.Wads, "headcrab", "any", 50).Hits, h => h.KeyValue == "models/headcrab.mdl");

        var sounds = AssetSearch.SearchGoldSrc(content.Files, content.Wads, "ambience/wind*", "sound", 50);
        Assert.Equal(["ambience/wind1.wav", "ambience/windy.wav"], sounds.Hits.Select(h => h.KeyValue));
        Assert.Equal("sprites/glow01.spr", Assert.Single(AssetSearch.SearchGoldSrc(content.Files, content.Wads, "glow", "sprite", 50).Hits).KeyValue);

        // textures by name, case-insensitive, the first WAD wins; decals come from decals.wad; fonts.wad is skipped
        var crate = Assert.Single(AssetSearch.SearchGoldSrc(content.Files, content.Wads, "crate", "texture", 50).Hits);
        Assert.Equal("crate01", crate.KeyValue);
        Assert.Equal(["mymod.wad", "halflife.wad"], crate.Sources.Select(Path.GetFileName));
        var scorch = Assert.Single(AssetSearch.SearchGoldSrc(content.Files, content.Wads, "scorch*", "any", 50).Hits);
        Assert.Equal(("decal", "{scorch1"), (scorch.Kind, scorch.KeyValue));
        Assert.Empty(AssetSearch.SearchGoldSrc(content.Files, content.Wads, "font1", "any", 50).Hits);
    }
}
