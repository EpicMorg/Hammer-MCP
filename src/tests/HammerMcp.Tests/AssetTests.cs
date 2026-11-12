using System.Text;
using HammerMcp.Core.Assets;

namespace HammerMcp.Tests;

public class AssetTests
{
    /// <summary>A minimal VPK v1 directory with the given files (no data).</summary>
    private static byte[] Vpk(params (string Dir, string Name, string Ext)[] files)
    {
        var tree = new MemoryStream();
        var w = new BinaryWriter(tree);
        void Str(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
        foreach (var ext in files.GroupBy(f => f.Ext))
        {
            Str(ext.Key);
            foreach (var dir in ext.GroupBy(f => f.Dir))
            {
                Str(dir.Key);
                foreach (var f in dir)
                {
                    Str(f.Name);
                    w.Write(0u); w.Write((ushort)0); w.Write((ushort)0x7fff); w.Write(0u); w.Write(0u); w.Write((ushort)0xffff);
                }
                Str("");
            }
            Str("");
        }
        Str("");
        var body = tree.ToArray();
        var ms = new MemoryStream();
        var h = new BinaryWriter(ms);
        h.Write(0x55AA1234u); h.Write(1u); h.Write((uint)body.Length); h.Write(body);
        return ms.ToArray();
    }

    [Fact]
    public void ReadsVpkDirectory()
    {
        var vpk = VpkDirectory.Read(Vpk(("materials/concrete", "floor01", "vmt"), ("models/props", "crate", "mdl"), (" ", "readme", "txt")));
        Assert.True(vpk.Contains(@"MATERIALS\Concrete\floor01.vmt"));
        Assert.True(vpk.Contains("models/props/crate.mdl"));
        Assert.True(vpk.Contains("readme.txt"));
        Assert.Equal(3, vpk.Files.Count);
    }

    [Fact]
    public void ResolvesGameinfoSearchPathsAndAssets()
    {
        var root = Directory.CreateTempSubdirectory("hammermcp-game").FullName;
        try
        {
            // <root>/hl2 (base game, loose + vpk), <root>/mymod (mod with custom/addon)
            Directory.CreateDirectory(Path.Combine(root, "hl2", "materials", "brick"));
            File.WriteAllText(Path.Combine(root, "hl2", "materials", "brick", "wall.vmt"), "");
            File.WriteAllBytes(Path.Combine(root, "hl2", "hl2_misc_dir.vpk"), Vpk(("models/props_c17", "oildrum001", "mdl")));
            Directory.CreateDirectory(Path.Combine(root, "mymod", "custom", "addon", "models"));
            File.WriteAllText(Path.Combine(root, "mymod", "custom", "addon", "models", "thing.mdl"), "");
            File.WriteAllText(Path.Combine(root, "mymod", "gameinfo.txt"), """
                "GameInfo"
                {
                	game	"My Mod"
                	FileSystem
                	{
                		SteamAppId	243730
                		SearchPaths
                		{
                			game+mod	|gameinfo_path|custom/*
                			game+mod	|gameinfo_path|.
                			game	|all_source_engine_paths|hl2/hl2_misc.vpk
                			game	|all_source_engine_paths|hl2
                			platform	|all_source_engine_paths|platform
                		}
                	}
                }
                """);
            var info = GameInfo.Load(Path.Combine(root, "mymod", "gameinfo.txt"), root);
            Assert.Equal(("My Mod", 243730), (info.Game, info.SteamAppId ?? 0));
            var assets = new AssetResolver(info);
            Assert.True(assets.Exists("materials/brick/wall.vmt"));
            Assert.True(assets.Exists("models/props_c17/oildrum001.mdl"));
            Assert.True(assets.Exists(@"models\thing.mdl"));
            Assert.False(assets.Exists("models/missing.mdl"));
            Assert.Empty(assets.Problems); // platform is not a "game" path and is not checked
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectsMapAssets()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            world
            {
            	"id" "1"
            	"classname" "worldspawn"
            	"skyname" "sky_day01_01"
            	solid
            	{
            		side
            		{
            			"id" "2"
            			"material" "BRICK/WALL"
            		}
            	}
            }
            entity
            {
            	"id" "3"
            	"classname" "prop_static"
            	"model" "models/props_c17/oildrum001.mdl"
            }
            entity
            {
            	"id" "4"
            	"classname" "info_overlay"
            	"material" "decals/graffiti01"
            }
            """);
        try
        {
            var refs = MapAssets.Collect(path);
            Assert.Contains(refs, r => r is { Kind: "material", Path: "materials/brick/wall.vmt" });
            Assert.Contains(refs, r => r is { Kind: "model", Path: "models/props_c17/oildrum001.mdl", ExampleEntityId: 3 });
            Assert.Contains(refs, r => r is { Kind: "overlay", Path: "materials/decals/graffiti01.vmt" });
            Assert.Equal(6, refs.Count(r => r.Kind == "skybox"));
            Assert.DoesNotContain(refs, r => r.Kind == "material" && r.Path.Contains("graffiti")); // overlay kv is not a brush face
        }
        finally
        {
            File.Delete(path);
        }
    }
}
