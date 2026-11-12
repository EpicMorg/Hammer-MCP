using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class AutosaveTests
{
    private static string Autosave(string original, int version) => $$"""
        versioninfo
        {
        	"editorversion" "400"
        	"mapversion" "{{version}}"
        	autosave
        	{
        		"originalname" "{{original}}"
        	}
        }
        """;

    [Fact]
    public void MatchesAutosavesByOriginalNameNewestFirst()
    {
        var root = Directory.CreateTempSubdirectory("hammermcp-autosave").FullName;
        try
        {
            var map = Path.Combine(root, "maps", "test.vmf");
            Directory.CreateDirectory(Path.GetDirectoryName(map)!);
            File.WriteAllText(map, "versioninfo\n{\n\"mapversion\" \"5\"\n}\n");
            File.SetLastWriteTime(map, DateTime.Now.AddMinutes(-10));
            var dir = Path.Combine(root, "autosave");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "test_001.vmf_autosave"), Autosave(map.Replace('\\', '/'), 6));
            File.SetLastWriteTime(Path.Combine(dir, "test_001.vmf_autosave"), DateTime.Now.AddMinutes(-5));
            File.WriteAllText(Path.Combine(dir, "test_002.vmf_autosave"), Autosave(map, 7));
            File.WriteAllText(Path.Combine(dir, "test_003.vmf_autosave"), Autosave(@"D:\other\test.vmf", 99)); // same name, other map

            var found = Autosaves.ForMap(dir, map);
            Assert.Equal([7, 6], found.Select(a => a.MapVersion ?? 0));
            Assert.Equal("test_002.vmf_autosave", Path.GetFileName(Autosaves.NewerThanMap(dir, map)!.Path));

            File.SetLastWriteTime(map, DateTime.Now.AddMinutes(1)); // saved after the autosaves
            Assert.Null(Autosaves.NewerThanMap(dir, map));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
