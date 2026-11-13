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
        """;

    [Fact]
    public void ResolvesTargetsByNameClassnameAndRuntimeClass()
    {
        var r = MapValidator.Validate(VmfEntities.Parse(new StringReader(Map)), new FgdParser().Parse(Fgd));
        Assert.Single(r.MissingTargets);
        Assert.Contains("nobody", r.MissingTargets[0].Detail);
        Assert.Empty(r.UnknownInputs);
        Assert.Single(r.QuotesInsideValues);
    }
}
