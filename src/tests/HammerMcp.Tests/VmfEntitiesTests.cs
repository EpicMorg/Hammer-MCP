using System.Numerics;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class VmfEntitiesTests
{
    private const string Map = """
        versioninfo
        {
        	"mapversion" "5"
        }
        world
        {
        	"id" "1"
        	"classname" "worldspawn"
        }
        entity
        {
        	"id" "10"
        	"classname" "info_target"
        	"targetname" "a"
        	"origin" "0 0 64"
        }
        entity
        {
        	"id" "11"
        	"classname" "info_target"
        	"targetname" "b"
        	"origin" "0 0 64.000001"
        	connections
        	{
        		"OnUser1" "x,Kill,,0,-1"
        	}
        }
        entity
        {
        	"id" "12"
        	"classname" "func_door"
        	solid
        	{
        		"id" "13"
        	}
        }
        hidden
        {
        	entity
        	{
        		"id" "20"
        		"classname" "light"
        		"origin" "128 -32 16.5"
        	}
        }
        """;

    private static List<VmfEntity> Parse() => VmfEntities.Parse(new StringReader(Map));

    [Fact]
    public void ReadsTopLevelAndHiddenEntities()
    {
        var all = Parse();
        Assert.Equal([10, 11, 12, 20], all.Select(e => e.Id));
        Assert.True(all.Single(e => e.Id == 20).Hidden);
        Assert.Equal(new Vector3(128, -32, 16.5f), all.Single(e => e.Id == 20).Origin);
        Assert.True(all.Single(e => e.Id == 12).HasBrushes);
        Assert.Null(all.Single(e => e.Id == 12).Origin);
        Assert.Equal("b", all.Single(e => e.Id == 11).TargetName);
        Assert.False(all.Single(e => e.Id == 11).KeyValues.ContainsKey("OnUser1")); // outputs live in connections
    }

    [Fact]
    public void AddressesWithinTolerance()
    {
        var all = Parse();
        Assert.Equal(2, all.At("info_target", new Vector3(0, 0, 64)).Count());
        Assert.Equal(2, all.At("INFO_TARGET", new Vector3(0, 0, 64.005f)).Count());
        Assert.Empty(all.At("info_target", new Vector3(0, 0, 65)));
        Assert.Single(all.At("light", new Vector3(128, -32, 16.5f)));
    }

    [Fact]
    public void JournalCountsEditsUntilTheMapIsSaved()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Map);
            var saved = VmfEntities.Read(path);
            var journal = new EditJournal();
            var p = new Vector3(256, 0, 0);
            Assert.Equal(0, journal.Count(path, saved, "light", p));
            journal.Created(path, "light", p);
            Assert.Equal(1, journal.Count(path, saved, "light", p));
            journal.Deleted(path, "info_target", new Vector3(0, 0, 64));
            Assert.Equal(1, journal.Count(path, saved, "info_target", new Vector3(0, 0, 64)));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1)); // "saved": the file now holds the edits
            Assert.Equal(0, journal.Count(path, saved, "light", p));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
