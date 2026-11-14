using System.Text;
using HammerMcp.Core.Assets;

namespace HammerMcp.Tests;

public class AssetSearchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hmcp-assets-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Loose(params string[] files)
    {
        var root = Path.Combine(_dir, "loose");
        foreach (var f in files)
        {
            var path = Path.Combine(root, f);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        return root;
    }

    /// <summary>A minimal VPK v1 directory file (names only) at pak_dir.vpk; returns the path gameinfo would list (pak.vpk).</summary>
    private string Vpk(params string[] files)
    {
        var tree = new MemoryStream();
        var w = new BinaryWriter(tree);
        void Str(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
        foreach (var ext in files.GroupBy(f => Path.GetExtension(f).TrimStart('.')))
        {
            Str(ext.Key);
            foreach (var dir in ext.GroupBy(f => Path.GetDirectoryName(f)!.Replace('\\', '/')))
            {
                Str(dir.Key);
                foreach (var f in dir)
                {
                    Str(Path.GetFileNameWithoutExtension(f));
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
        File.WriteAllBytes(Path.Combine(_dir, "pak_dir.vpk"), ms.ToArray());
        return Path.Combine(_dir, "pak.vpk");
    }

    [Fact]
    public void FindsLooseAndPackedLooseFirst()
    {
        var loose = Loose("models/props/cabinet01.mdl", "materials/custom/wall.vmt");
        var vpk = Vpk("models/props/cabinet01.mdl", "models/props_2fort/locker001.mdl", "models/props/chair.mdl");
        var assets = new AssetResolver([loose, vpk]);

        var r = AssetSearch.Search(assets, AssetSearch.Source1, "cabinet", "model", 50);
        var hit = Assert.Single(r.Hits);
        Assert.Equal("models/props/cabinet01.mdl", hit.KeyValue);
        Assert.Equal([loose, Path.Combine(_dir, "pak_dir.vpk")], hit.Sources);

        var wild = AssetSearch.Search(assets, AssetSearch.Source1, "models/*/locker*", null, 50);
        Assert.Equal("models/props_2fort/locker001.mdl", Assert.Single(wild.Hits).Path);
    }

    [Fact]
    public void KeyValuesAreWrittenAsHammerWantsThem()
    {
        var loose = Loose("materials/custom/wall.vmt", "materials/custom/wall.vtf", "sound/ambient/wind.wav",
            "materials/sprites/glow01.vmt");
        var assets = new AssetResolver([loose]);
        var all = AssetSearch.Search(assets, AssetSearch.Source1, "", "any", 50).Hits.ToDictionary(h => h.Path);

        Assert.Equal(("material", "custom/wall"), (all["materials/custom/wall.vmt"].Kind, all["materials/custom/wall.vmt"].KeyValue));
        Assert.Equal(("texture", "materials/custom/wall.vtf"), (all["materials/custom/wall.vtf"].Kind, all["materials/custom/wall.vtf"].KeyValue));
        Assert.Equal(("sound", "ambient/wind.wav"), (all["sound/ambient/wind.wav"].Kind, all["sound/ambient/wind.wav"].KeyValue));
        Assert.Equal(("sprite", "sprites/glow01.vmt"), (all["materials/sprites/glow01.vmt"].Kind, all["materials/sprites/glow01.vmt"].KeyValue));
        Assert.Equal(4, all.Count);

        Assert.Equal("ambient/wind.wav", Assert.Single(AssetSearch.Search(assets, AssetSearch.Source1, "wind", "sound", 50).Hits).KeyValue);
        // a wildcard may start at any folder of the path
        Assert.Equal("sound/ambient/wind.wav", Assert.Single(AssetSearch.Search(assets, AssetSearch.Source1, "ambient/wind*", null, 50).Hits).Path);
        Assert.Empty(AssetSearch.Search(assets, AssetSearch.Source1, "bient/wind*", null, 50).Hits);
    }

    [Fact]
    public void Source2CompiledFilesAreReportedAsSources()
    {
        var vpk = Vpk("models/props/crate.vmdl_c", "materials/dev/grid.vmat_c", "sounds/ui/click.vsnd_c");
        var assets = new AssetResolver([vpk]);
        var hits = AssetSearch.Search(assets, AssetSearch.Source2, "", null, 50).Hits.Select(h => (h.Kind, h.KeyValue)).Order().ToList();
        Assert.Equal([("material", "materials/dev/grid.vmat"), ("model", "models/props/crate.vmdl"), ("sound", "sounds/ui/click.vsnd")], hits);
    }

    [Fact]
    public void LimitKeepsTheTotal()
    {
        var assets = new AssetResolver([Loose("models/a1.mdl", "models/a2.mdl", "models/a3.mdl")]);
        var r = AssetSearch.Search(assets, AssetSearch.Source1, "models/a", "model", 2);
        Assert.Equal(3, r.Total);
        Assert.Equal(2, r.Hits.Count);
    }
}
