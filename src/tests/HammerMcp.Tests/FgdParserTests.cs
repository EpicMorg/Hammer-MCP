using HammerMcp.Core.Fgd;

namespace HammerMcp.Tests;

public class FgdParserTests
{
    private const string Base = """
        // comment
        @mapsize(-16384, 16384)
        @BaseClass = Targetname
        [
        	targetname(target_source) : "Name" : : "The name that other entities refer to this entity by."
        	input Kill(void) : "Removes this entity from the world."
        	output OnUser1(void) : "Fired in response to FireUser1 input."
        ]
        @BaseClass base(Targetname) color(0 0 200) = Item
        [
        	fadescale(float) : "Fade Scale" : 1 : "Part one" +
        		" part two." +

        	spawnflags(Flags) =
        	[
        		1 : "Start Constrained" : 0
        		2 : "Deny player pickup" : 1
        	]
        ]
        """;

    private const string Game = """
        @include "base.fgd"
        @version(5)
        @PointClass base(Item) studio("models/items/boxsrounds.mdl") iconsprite("editor/item.vmt") = item_ammo_pistol : "Pistol ammo"
        [
        	rendermode(choices) readonly : "Render Mode" : 0 : "Used to set a non-standard rendering mode." =
        	[
        		0 : "Normal"
        		"1" : "Color"
        	]
        	model(*studio) : "World model" : "" : "GMod-style type with an asterisk"
        ]
        @SolidClass = worldspawn : "The world." : "http://example.org/help"
        [
        	skyname(string) : "SkyBox Texture Name" : "sky_day01_01"
        ]
        @ExtendClass = item_ammo_pistol
        [
        	output OnExtended(void) : "Added by Hammer++ ExtendClass"
        ]
        @ExtendClass = not_defined_anywhere [ ]
        @AutoVisGroup = "Brushes" [ "Triggers" [ "trigger_once" ] ]
        @Bogus stuff here
        @PointClass base(Item) = item_ammo_pistol : "Redefined: replaces the earlier class" []
        """;

    private static FgdDocument Parse()
    {
        var parser = new FgdParser(name => name == "base.fgd" ? WriteTemp(Base) : null);
        return parser.Parse(Game, "game.fgd");
    }

    private static string WriteTemp(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hammermcp-{Guid.NewGuid():N}.fgd");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ReadsHeaderCommandsAndIncludes()
    {
        var doc = Parse();
        Assert.Equal(5, doc.Version);
        Assert.Equal((-16384, 16384), doc.MapSize);
        Assert.Contains("base.fgd", doc.Includes);
        Assert.Contains(doc.Warnings, w => w.Contains("@Bogus"));
        Assert.DoesNotContain(doc.Warnings, w => w.Contains("AutoVisGroup"));
    }

    [Fact]
    public void ReadsPropertiesChoicesFlagsAndConcatenatedStrings()
    {
        var item = Parse().Get("Item")!;
        var fade = item.Properties.Single(p => p.Name == "fadescale");
        Assert.Equal("float", fade.Type);
        Assert.Equal("1", fade.DefaultValue);
        Assert.Equal("Part one part two.", fade.Description);
        var flags = item.Properties.Single(p => p.Name == "spawnflags");
        Assert.Equal("flags", flags.Type);
        Assert.Equal([false, true], flags.Options.Select(o => o.On == true));
    }

    [Fact]
    public void RedefinitionReplacesLikeValveFgdlib()
    {
        var ammo = Parse().Get("item_ammo_pistol")!;
        Assert.Equal("Redefined: replaces the earlier class", ammo.Description);
        Assert.DoesNotContain(ammo.Properties, p => p.Name is "rendermode" or "model"); // nothing left of the first definition
        Assert.Contains(ammo.Properties, p => p.Name == "targetname");                 // its own bases are still inherited
    }

    [Fact]
    public void ExtendClassMergesAndInheritanceResolves()
    {
        var parser = new FgdParser(name => name == "base.fgd" ? WriteTemp(Base) : null);
        var doc = parser.Parse(Game.Replace("@PointClass base(Item) = item_ammo_pistol : \"Redefined: replaces the earlier class\" []", ""), "game.fgd");
        var ammo = doc.Get("item_ammo_pistol")!;
        Assert.Contains(ammo.Outputs, o => o.Name == "OnExtended");
        Assert.Equal("studio", ammo.Properties.Single(p => p.Name == "model").Type);
        Assert.True(ammo.Properties.Single(p => p.Name == "rendermode").ReadOnly);
        Assert.Equal(["0", "1"], ammo.Properties.Single(p => p.Name == "rendermode").Options.Select(o => o.Value));
        Assert.Contains(ammo.Behaviours, b => b.Name == "studio" && b.Arguments.Contains("boxsrounds"));
        Assert.Null(doc.Get("not_defined_anywhere"));

        var resolved = doc.Resolve("item_ammo_pistol")!;
        Assert.Contains(resolved.Properties, p => p.Name == "targetname");   // from Targetname via Item
        Assert.Contains(resolved.Properties, p => p.Name == "spawnflags");   // from Item
        Assert.Contains(resolved.Inputs, i => i.Name == "Kill");
        Assert.Contains(resolved.Behaviours, b => b.Name == "color");        // inherited helper
    }

    [Fact]
    public void ClassHelpUrlIsSkipped() =>
        Assert.Equal("The world.", Parse().Get("worldspawn")!.Description);

    [Fact]
    public void ReadsSource2Syntax()
    {
        const string fgd = """
            @PointClass base(Targetname) iconsprite("x.vmat")
            	{
            		entity_tool_name = "Player Start"
            		entity_tool_group = "Player"
            	}
            = info_player_start : "Start"
            [
            	fogcolor(color255) [ group = "Fog Color" ] : "Fog Color" : "255 255 255" : "Say \"hi\" here."
            	dof_near(float) { enabled={ variable="dof_enabled" value="1" } group="DOF" } : "Near" : "50"
            	input SetColor(vector) [ group = "x" ] : "Set color"
            ]
            @OverrideClass = info_player_start
            [
            	extra(string) : "Extra"
            ]
            @ModelGameData = prop_data [ base(x) ]
            @helpinfo "x" [ ]
            """;
        var doc = new FgdParser().Parse(fgd);
        Assert.Empty(doc.Warnings);
        var cls = doc.Get("info_player_start")!;
        Assert.Equal("Player", cls.Metadata["entity_tool_group"]);
        Assert.Equal(["fogcolor", "dof_near", "extra"], cls.Properties.Select(p => p.Name));
        Assert.Equal("Say \"hi\" here.", cls.Properties[0].Description);
        Assert.Equal("SetColor", Assert.Single(cls.Inputs).Name);
    }
}
