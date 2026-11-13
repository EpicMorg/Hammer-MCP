using HammerMcp.Core.Fgd;
using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class MapValidatorTests
{
    private const string Fgd = """
        @PointClass = logic_relay : "Relay" [ input Trigger(void) : "" output OnTrigger(void) : "" ]
        @PointClass = point_servercommand : "Server command" [ input Command(string) : "" ]
        @PointClass = player : "Player" [ input SetHealth(integer) : "" ]
        """;

    private const string Map = """
        entity
        {
            "id" "1"
            "classname" "logic_relay"
            "targetname" "relay"
            connections
            {
                "OnTrigger" "point_servercommand,Command,echo hi,0,-1"
                "OnTrigger" "player,SetHealth,10,0,-1"
                "OnTrigger" "relay*,Trigger,,0,-1"
                "OnTrigger" "nobody,Trigger,,0,-1"
                "OnTrigger" "point_servercommand,Command,echo "hi",0,-1"
            }
        }
        entity
        {
            "id" "2"
            "classname" "point_servercommand"
        }
        entity
        {
            "id" "3"
            "classname" "logic_relay"
            "targetname" "kv_relay"
            "OnTrigger" "ghost,Trigger,,0,-1"
        }
        """;

    [Fact]
    public void ResolvesTargetsByNameClassnameAndRuntimeClass()
    {
        var r = MapValidator.Validate(VmfEntities.Parse(new StringReader(Map)), new FgdParser().Parse(Fgd));
        Assert.Equal(2, r.MissingTargets.Count);
        Assert.Contains(r.MissingTargets, i => i.Detail.Contains("nobody"));
        Assert.Contains(r.MissingTargets, i => i.Id == 3 && i.Detail.Contains("ghost")); // the keyvalue output is checked too
        Assert.Equal(3, Assert.Single(r.OutputsAsKeyValues).Id);
        Assert.Empty(r.UnknownKeyValues);
        Assert.Empty(r.UnknownInputs);
        Assert.Single(r.QuotesInsideValues);
    }
}
