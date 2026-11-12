using System.Text;
using HammerMcp.Core.Config;
using HammerMcp.Core.Maps;
using HammerMcp.Core.Materials;

namespace HammerMcp.Tests;

public class FormatsTests
{
    [Theory]
    [InlineData("door,Open,,1.5,-1")]
    [InlineData("door\u001bOpen\u001b\u001b1.5\u001b-1")]
    public void ParsesConnectionsWithCommaOrEscSeparators(string value)
    {
        var c = VmfConnection.Parse("OnTrigger", value)!;
        Assert.Equal(("OnTrigger", "door", "Open", "", 1.5f, -1), (c.Output, c.Target, c.Input, c.Parameter, c.Delay, c.TimesToFire));
    }

    [Fact]
    public void VmfEntitiesKeepOutputs()
    {
        const string vmf = """
            entity
            {
            	"id" "5"
            	"classname" "trigger_once"
            	connections
            	{
            		"OnTrigger" "door,Open,,0,1"
            		"OnTrigger" "lamp,TurnOn,,0.5,-1"
            	}
            	solid
            	{
            		"id" "6"
            	}
            }
            """;
        var e = VmfEntities.Parse(new StringReader(vmf)).Single();
        Assert.True(e.HasBrushes);
        Assert.Equal(["door", "lamp"], e.Connections.Select(c => c.Target));
    }

    [Fact]
    public void ReadsHammerGameConfig()
    {
        const string cfg = """
            "Configs"
            {
            	"Games"
            	{
            		"My Mod"
            		{
            			"GameDir"		"C:\Steam\steamapps\sourcemods\mymod"
            			"Hammer"
            			{
            				"GameData0"		"C:\SDK\bin\halflife2.fgd"
            				"GameData1"		"C:\Steam\steamapps\sourcemods\mymod\mymod.fgd"
            				"BSP"		"C:\SDK\bin\vbsp.exe"
            				"MapDir"		"C:\maps\mymod"
            			}
            		}
            		"Other"
            		{
            			"GameDir"		"C:\Other"
            			"Hammer" { "MapDir" "C:\maps\other" }
            		}
            	}
            	"SDKVersion"		"5"
            }
            """;
        var configs = HammerGameConfig.Parse(new MemoryStream(Encoding.UTF8.GetBytes(cfg)));
        Assert.Equal(2, configs.Count);
        Assert.Equal(["C:\\SDK\\bin\\halflife2.fgd", "C:\\Steam\\steamapps\\sourcemods\\mymod\\mymod.fgd"], configs[0].FgdFiles);
        Assert.Equal("C:\\SDK\\bin\\vbsp.exe", configs[0].Tools["BSP"]);
        Assert.Equal("Other", HammerGameConfig.ForMap(configs, @"C:\maps\other\test.vmf")!.Name);
    }

    [Fact]
    public void ReadsVmt()
    {
        const string vmt = """
            "LightmappedGeneric"
            {
            	"$basetexture" "concrete/concretefloor001a"
            	"$surfaceprop" "concrete"
            	"Proxies"
            	{
            		"TextureScroll" { "texturescrollvar" "$basetexturetransform" }
            	}
            }
            """;
        var m = VmtMaterial.Parse(new MemoryStream(Encoding.UTF8.GetBytes(vmt)));
        Assert.Equal("LightmappedGeneric", m.Shader);
        Assert.Equal("concrete/concretefloor001a", m.BaseTexture);
        Assert.Contains("TextureScroll", m.Blocks["Proxies"]);
    }
}
