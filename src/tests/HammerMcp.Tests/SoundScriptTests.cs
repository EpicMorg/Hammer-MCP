using System.Text;
using HammerMcp.Core.Assets;

namespace HammerMcp.Tests;

public class SoundScriptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hammermcp-sounds").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A VPK v2 dir file with one file of the given data: <paramref name="preload"/> bytes in the tree, the rest in
    /// the dir file itself (archive 0x7FFF) or in archive x_000.vpk.</summary>
    private static (byte[] Dir, byte[] Archive) Vpk(string dir, string name, string ext, byte[] data, int preload, bool inDirFile)
    {
        var rest = data[preload..];
        var tree = new MemoryStream();
        var w = new BinaryWriter(tree);
        void Str(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
        Str(ext); Str(dir); Str(name);
        w.Write(0u); w.Write((ushort)preload); w.Write(inDirFile ? (ushort)0x7fff : (ushort)0); w.Write(inDirFile ? 4u : 0u); w.Write((uint)rest.Length); w.Write((ushort)0xffff);
        w.Write(data, 0, preload);
        Str(""); Str(""); Str("");
        var body = tree.ToArray();
        var ms = new MemoryStream();
        var h = new BinaryWriter(ms);
        h.Write(0x55AA1234u); h.Write(2u); h.Write((uint)body.Length); h.Write(new byte[16]); h.Write(body);
        if (inDirFile)
        {
            h.Write(new byte[4]); // other data before ours: offset 4
            h.Write(rest);
        }
        return (ms.ToArray(), inDirFile ? [] : rest);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(3, false)]
    [InlineData(0, false)]
    public void ReadsFileDataFromVpk(int preload, bool inDirFile)
    {
        var data = Encoding.UTF8.GetBytes("\"Ambient.Wind\" { \"wave\" \"ambient/wind.wav\" }");
        var (dir, archive) = Vpk("scripts", "game_sounds_test", "txt", data, preload, inDirFile);
        File.WriteAllBytes(Path.Combine(_root, "pak01_dir.vpk"), dir);
        if (!inDirFile)
            File.WriteAllBytes(Path.Combine(_root, "pak01_000.vpk"), archive);
        var vpk = VpkDirectory.Open(Path.Combine(_root, "pak01.vpk"))!;
        Assert.Equal(data, vpk.ReadFile(@"Scripts\GAME_SOUNDS_TEST.txt"));
        Assert.Null(vpk.ReadFile("scripts/other.txt"));
    }

    private GameInfo GameInfoWith(params string[] searchPaths)
    {
        var lines = string.Join("\n", searchPaths.Select(p => $"\t\t\t{p}"));
        var path = Write("mod/gameinfo.txt", $"\"GameInfo\"\n{{\n\tgame \"Mod\"\n\tFileSystem\n\t{{\n\t\tSearchPaths\n\t\t{{\n{lines}\n\t\t}}\n\t}}\n}}\n");
        return GameInfo.Load(path, _root);
    }

    [Fact]
    public void Pak01IsMountedOnlyWhenGameinfoListsNoVpks()
    {
        // Portal 2 / CS:GO: gameinfo lists only "Game |gameinfo_path|.", the engine mounts pak01_dir.vpk of it, ahead of loose files
        var (dir, _) = Vpk("sound/ambient", "drone", "wav", [1, 2, 3], 0, inDirFile: true);
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllBytes(Path.Combine(_root, "mod", "pak01_dir.vpk"), dir);
        Write("mod/sound/ambient/drone.wav", "loose");
        var l4d = new AssetResolver(GameInfoWith("Game |gameinfo_path|."));
        Assert.Single(l4d.Vpks);
        Assert.Equal([1, 2, 3], l4d.Read("sound/ambient/drone.wav")!.Value.Data);
        Assert.EndsWith("pak01_dir.vpk", l4d.Locate("sound/ambient/drone.wav"));

        // Source 2013: VPKs are listed; a pak01_dir.vpk the gameinfo does not name is not mounted
        Directory.CreateDirectory(Path.Combine(_root, "hl2"));
        var sdk = new AssetResolver(GameInfoWith("game |all_source_engine_paths|hl2/hl2_misc.vpk", "game+mod |gameinfo_path|."));
        Assert.DoesNotContain(sdk.Vpks, v => v.EndsWith("pak01_dir.vpk"));
        Assert.Equal("loose"u8.ToArray(), sdk.Read("sound/ambient/drone.wav")!.Value.Data);
    }

    [Fact]
    public void SearchOrderFollowsTheGameinfo()
    {
        // TF2: "game+mod tf/tf2_misc.vpk" is listed before the tf folder, so the VPK copy wins over a loose one
        var (dir, _) = Vpk("scripts", "a", "txt", "packed"u8.ToArray(), 0, inDirFile: true);
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllBytes(Path.Combine(_root, "mod", "misc_dir.vpk"), dir);
        Write("mod/scripts/a.txt", "loose");
        var vpkFirst = new AssetResolver(GameInfoWith("game |gameinfo_path|misc.vpk", "game |gameinfo_path|.", "game |gameinfo_path|misc.vpk"));
        Assert.Equal("packed"u8.ToArray(), vpkFirst.Read("scripts/a.txt")!.Value.Data);
        Assert.Single(vpkFirst.Vpks);
        var looseFirst = new AssetResolver(GameInfoWith("game |gameinfo_path|.", "game |gameinfo_path|misc.vpk"));
        Assert.Equal("loose"u8.ToArray(), looseFirst.Read("scripts/a.txt")!.Value.Data);
        var sources = looseFirst.List("scripts").Select(f => f.Source).ToList();
        Assert.Equal(2, sources.Count);
        Assert.False(sources[0].EndsWith(".vpk"));
        Assert.EndsWith("misc_dir.vpk", sources[1]);
    }

    [Fact]
    public void MissingVpkArchiveReadsAsNull()
    {
        var (dir, _) = Vpk("scripts", "x", "txt", [1, 2, 3, 4], 0, inDirFile: false);
        File.WriteAllBytes(Path.Combine(_root, "pak01_dir.vpk"), dir);
        Assert.Null(VpkDirectory.Open(Path.Combine(_root, "pak01_dir.vpk"))!.ReadFile("scripts/x.txt"));
    }

    [Fact]
    public void ReadsFileDataFromPak()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var data = "hello"u8.ToArray();
        w.Write("PACK"u8.ToArray()); w.Write(12 + data.Length); w.Write(64);
        w.Write(data);
        var name = new byte[56];
        Encoding.ASCII.GetBytes("sound/a.wav").CopyTo(name, 0);
        w.Write(name); w.Write(12); w.Write(data.Length);
        var path = Path.Combine(_root, "pak0.pak");
        File.WriteAllBytes(path, ms.ToArray());
        Assert.Equal(data, PakFile.Open(path).ReadFile("SOUND/a.wav"));
    }

    [Theory]
    [InlineData("ambient/wind.wav", "sound/ambient/wind.wav")]
    [InlineData(")weapons/pistol/fire1.wav", "sound/weapons/pistol/fire1.wav")]
    [InlineData("*#music/song.mp3", "sound/music/song.mp3")]
    [InlineData("^@<>~&$}(?+npc/x.wav", "sound/npc/x.wav")]
    [InlineData(@"common\null.wav", "sound/common/null.wav")]
    [InlineData("!HG_ALERT1", null)]
    [InlineData("  ", null)]
    public void WaveFileStripsSoundChars(string wave, string? expected) => Assert.Equal(expected, SoundScripts.WaveFile(wave));

    private void WriteGame()
    {
        Write("scripts/game_sounds_manifest.txt", """
            // comment
            "game_sounds_manifest"
            {
            	"precache_file"	"scripts/game_sounds.txt"
            	"preload_file"	"scripts/game_sounds_ambient.txt"
            	"precache_file"	"scripts/game_sounds_missing.txt"
            	"precache_file"	"scripts/game_sounds.txt"
            }
            """);
        Write("scripts/game_sounds.txt", """
            #include "game_sounds_included.txt"
            "Weapon_Pistol.Single"
            {
            	"channel"	"CHAN_WEAPON"
            	"volume"	"0.55"
            	"soundlevel"	"SNDLVL_GUNFIRE"
            	"rndwave"	[$WIN32]
            	{
            		"wave"	")weapons/pistol/pistol_fire2.wav"
            		"wave"	")weapons/pistol/pistol_fire3.wav"
            	}
            }
            Ambient.Wind { channel CHAN_STATIC wave "ambient/wind/wind1.wav" }
            """);
        Write("scripts/game_sounds_included.txt", """
            "Included.Sound" { "wave" "common/null.wav" }
            """);
        Write("scripts/game_sounds_ambient.txt", """
            "Ambient.Wind" { "wave" "ambient/other.wav" }
            "Ambient.Drone"
            {
            	"operator_stacks" { "start_stack" { "import_stack" "P2_null_start" } }
            	"wave" "*ambient/drone.wav"
            }
            """);
        Write("sound/ambient/wind/wind1.wav", "");
    }

    [Fact]
    public void LoadsScriptsFromTheManifest()
    {
        WriteGame();
        var scripts = SoundScripts.Load(new AssetResolver([_root]));
        Assert.Equal(["scripts/game_sounds.txt", "scripts/game_sounds_ambient.txt"], scripts.Scripts);
        Assert.Contains(scripts.Problems, p => p.Contains("game_sounds_missing.txt"));
        Assert.Equal(4, scripts.Entries.Count);
        var pistol = scripts.Find("weapon_pistol.single")!;
        Assert.Equal("CHAN_WEAPON", pistol.Channel);
        Assert.Equal([")weapons/pistol/pistol_fire2.wav", ")weapons/pistol/pistol_fire3.wav"], pistol.Waves);
        Assert.Equal(["ambient/wind/wind1.wav"], scripts.Find("Ambient.Wind")!.Waves); // the first definition wins
        Assert.Equal(1, scripts.Duplicates);
        Assert.True(scripts.Contains("Included.Sound"));
        Assert.Equal(["*ambient/drone.wav"], scripts.Find("Ambient.Drone")!.Waves);
    }

    [Fact]
    public void LoadsTheMapsLevelSounds()
    {
        WriteGame();
        Write("maps/d1_trainstation_01_level_sounds.txt", "\"Trainstation.Announcer\" { \"wave\" \"*vo/announcer.wav\" }");
        var assets = new AssetResolver([_root]);
        var level = SoundScripts.LoadLevel(assets, "d1_trainstation_01")!;
        Assert.True(level.Contains("Trainstation.Announcer"));
        Assert.Equal(["maps/d1_trainstation_01_level_sounds.txt"], level.Scripts);
        Assert.False(SoundScripts.Load(assets).Contains("Trainstation.Announcer"));
        Assert.Null(SoundScripts.LoadLevel(assets, "other_map"));
    }

    [Fact]
    public void WithoutManifestReadsEveryGameSoundsFile()
    {
        Write("scripts/game_sounds_b.txt", "\"B.Sound\" { \"wave\" \"b.wav\" }");
        Write("scripts/game_sounds_a.txt", "\"A.Sound\" { \"wave\" \"a.wav\" }");
        Write("scripts/surfaceproperties.txt", "\"metal\" { \"bulletimpact\" \"x\" }");
        var scripts = SoundScripts.Load(new AssetResolver([_root]));
        Assert.Equal(["scripts/game_sounds_a.txt", "scripts/game_sounds_b.txt"], scripts.Scripts);
        Assert.Single(scripts.Problems);
    }

    [Fact]
    public void FindAssetsSearchesSoundScriptNames()
    {
        WriteGame();
        var assets = new AssetResolver([_root]);
        var scripts = SoundScripts.Load(assets);
        var wind = AssetSearch.Search(assets, AssetSearch.Source1, "wind", "any", 50, scripts).Hits;
        Assert.Contains(wind, h => h is { Kind: "sound", KeyValue: "ambient/wind/wind1.wav" });
        var script = Assert.Single(wind, h => h.Kind == "soundscript");
        Assert.Equal(("Ambient.Wind", "Ambient.Wind", "scripts/game_sounds.txt"), (script.Path, script.KeyValue, script.Sources[0]));
        Assert.Equal(["ambient/wind/wind1.wav"], script.Waves);
        Assert.Equal(["Weapon_Pistol.Single"], AssetSearch.Search(assets, AssetSearch.Source1, "weapon_*", "soundscript", 50, scripts).Hits.Select(h => h.KeyValue));
        Assert.DoesNotContain(AssetSearch.Search(assets, AssetSearch.Source1, "wind", "sound", 50, scripts).Hits, h => h.Kind == "soundscript");
    }

    [Fact]
    public void CheckAssetsCollectsSoundsAndScriptNames()
    {
        var vmf = Write("map.vmf", """
            entity
            {
            	"id" "5"
            	"classname" "ambient_generic"
            	"message" "Ambient.Wind"
            }
            entity
            {
            	"id" "6"
            	"classname" "ambient_generic"
            	"message" ")ambient/Machines/hum.wav"
            }
            entity
            {
            	"id" "7"
            	"classname" "func_door"
            	"noise1" "doors/door1_move.wav"
            	"message" "not a sound"
            }
            """);
        var refs = MapAssets.Collect(vmf);
        Assert.Contains(refs, r => r is { Kind: "soundscript", Path: "Ambient.Wind", ExampleEntityId: 5 });
        Assert.Contains(refs, r => r is { Kind: "sound", Path: "sound/ambient/machines/hum.wav", ExampleEntityId: 6 });
        Assert.Contains(refs, r => r is { Kind: "sound", Path: "sound/doors/door1_move.wav", ExampleEntityId: 7 });
        Assert.Equal(3, refs.Count);
    }

    /// <summary>A real Source 1 game folder (e.g. ...\Half-Life 2\hl2) with its sound scripts in VPKs; skipped when not set.</summary>
    [Fact]
    public void ReadsSoundScriptsOfARealGame()
    {
        var gameDir = Environment.GetEnvironmentVariable("HAMMER_MCP_SOURCE_GAME");
        Assert.SkipWhen(gameDir is null || !File.Exists(Path.Combine(gameDir, "gameinfo.txt")), "HAMMER_MCP_SOURCE_GAME not set");
        var assets = new AssetResolver(GameInfo.Load(Path.Combine(gameDir!, "gameinfo.txt"), Path.GetDirectoryName(gameDir)));
        var scripts = SoundScripts.Load(assets);
        Assert.True(scripts.Entries.Count > 500, $"{scripts.Entries.Count} entries from {scripts.Scripts.Count} scripts; {string.Join("; ", scripts.Problems)}");
        Assert.All(scripts.Entries.Take(50), e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
    }
}
