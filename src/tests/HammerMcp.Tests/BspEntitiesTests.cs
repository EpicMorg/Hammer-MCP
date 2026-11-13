using System.Text;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class BspEntitiesTests
{
    [Fact]
    public void ReadsEntityLumpWithRepeatedOutputKeys()
    {
        const string lump = "{\n\"classname\" \"worldspawn\"\n}\n{\n\"targetname\" \"relay\"\n\"OnTrigger\" \"a,TurnOff,,0,-1\"\n\"OnTrigger\" \"b\u001bTurnOn\u001b\u001b0\u001b-1\"\n\"classname\" \"logic_relay\"\n}\n";
        var bytes = Encoding.UTF8.GetBytes(lump + "\0");
        var path = Path.Combine(Path.GetTempPath(), $"hammermcp-{Guid.NewGuid():N}.bsp");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(Encoding.ASCII.GetBytes("VBSP"));
            w.Write(20);
            w.Write(8 + 64 * 16 + 4); // entity lump right after the header (64 lumps + map revision)
            w.Write(bytes.Length);
            w.Write(new byte[8 + 63 * 16 + 4]);
            w.Write(bytes);
        }
        var r = BspEntities.Read(path);
        Assert.Equal(20, r.Version);
        Assert.Equal(2, r.Entities.Count);
        var relay = r.Entities[1];
        Assert.Equal("logic_relay", relay.ClassName);
        Assert.Equal(2, relay.KeyValues.Count(kv => kv.Key == "OnTrigger"));
        Assert.NotNull(VmfConnection.Parse("OnTrigger", relay.KeyValues[2].Value));
    }
}
