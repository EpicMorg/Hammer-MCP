using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class VmfSummaryTests
{
    private const string Map = """
        versioninfo
        {
        	"editorversion" "400"
        	"editorbuild" "8873"
        	"mapversion" "258"
        	"formatversion" "100"
        	"prefab" "0"
        }
        visgroups
        {
        	visgroup
        	{
        		"name" "lights"
        		"visgroupid" "1"
        	}
        }
        world
        {
        	"id" "1"
        	"classname" "worldspawn"
        	"skyname" "sky_day01_01"
        	solid
        	{
        		"id" "2"
        		side
        		{
        			"id" "3"
        		}
        	}
        	solid
        	{
        		"id" "4"
        	}
        }
        entity
        {
        	"id" "10"
        	"classname" "light"
        	"targetname" "mcp_light"
        }
        entity
        {
        	"id" "11"
        	"classname" "func_door"
        	solid
        	{
        		"id" "12"
        	}
        }
        entity
        {
        	"id" "13"
        	"classname" "light"
        	connections
        	{
        		"OnTrigger" "door,Open,,0,-1"
        	}
        }
        hidden
        {
        	entity
        	{
        		"id" "14"
        		"classname" "info_target"
        	}
        }
        """;

    [Fact]
    public void CountsAndKeys()
    {
        var s = VmfSummary.Parse(new StringReader(Map.ReplaceLineEndings("\r\n")));
        Assert.Equal(258, s.MapVersion);
        Assert.Equal(8873, s.EditorBuild);
        Assert.Equal(100, s.FormatVersion);
        Assert.False(s.Prefab);
        Assert.Equal("sky_day01_01", s.SkyName);
        Assert.Equal(3, s.Entities);           // hidden entity is not top-level
        Assert.Equal(1, s.BrushEntities);
        Assert.Equal(2, s.PointEntities);
        Assert.Equal(2, s.WorldBrushes);
        Assert.Equal(3, s.Brushes);
        Assert.Equal(2, s.EntityClasses);
        Assert.Equal(1, s.Visgroups);
        Assert.Equal(1, s.HiddenObjects);
        Assert.Equal(new KeyValuePair<string, int>("light", 2), s.Classes[0]);
    }
}
