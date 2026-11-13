using HammerMcp.Core.Config;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

/// <summary>Portal: Revolution (Strata) layout: bin\win64\hammer.exe, hammer\cfg\gameconfig.txt with "..\.." paths, HammerConfig.vdf.</summary>
public class StrataTests
{
    [Fact]
    public void ReadsConfigWithDotDotPathsAndAutosaveDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammermcp-strata-{Guid.NewGuid():N}");
        var win64 = Path.Combine(root, "bin", "win64");
        var cfg = Path.Combine(root, "hammer", "cfg");
        Directory.CreateDirectory(win64);
        Directory.CreateDirectory(cfg);
        Directory.CreateDirectory(Path.Combine(root, "revolution"));
        File.WriteAllText(Path.Combine(root, "revolution", "gameinfo.txt"), "");
        var w = win64.Replace('/', '\\');
        File.WriteAllText(Path.Combine(cfg, "gameconfig.txt"), $$"""
            "Configs"
            {
            	"Games"
            	{
            		"Portal: Revolution"
            		{
            			"GameDir"		"{{w}}\..\..\revolution"
            			"Hammer"
            			{
            				"GameData0"		"{{w}}\..\..\revolution\revolution.fgd"
            				"MapDir"		"{{w}}\..\..\sdk_content\maps"
            			}
            		}
            	}
            }
            """);
        File.WriteAllText(Path.Combine(cfg, "HammerConfig.vdf"), """
            <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
            {
            	General =
            	{
            		"Autosaves Enabled" = 1
            		"Autosave Dir" = "\\autosave\\"
            	}
            }
            """);
        var exe = Path.Combine(win64, "hammer.exe");
        var (file, configs) = HammerGameConfig.ForExe(exe);
        Assert.NotNull(file);
        var c = Assert.Single(configs);
        Assert.Equal("Portal: Revolution", c.Name);
        Assert.Same(c, HammerGameConfig.ForMap([c, c with { Name = "other", MapDir = @"Z:\elsewhere" }], (root + @"\sdk_content\maps\sp_test.vmf").Replace('/', '\\')));
        // as shipped: "\autosave\" - Hammer rejects it (not absolute) and disables autosaves
        var (dir, enabled) = Autosaves.StrataFolder(exe);
        Assert.False(enabled);
        Assert.Equal(@"\autosave\", dir);
        File.WriteAllText(Path.Combine(cfg, "HammerConfig.vdf"), """
            { General = { "Autosaves Enabled" = 1 "Autosave Dir" = "D:\\maps\\autosave\\" } }
            """);
        Assert.Equal((@"D:\maps\autosave\", true), Autosaves.StrataFolder(exe));
    }
}
