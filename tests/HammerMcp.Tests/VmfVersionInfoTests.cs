using HammerMcp.Core.Maps;

namespace HammerMcp.Tests;

public class VmfVersionInfoTests
{
    private const string Head = """
        versioninfo
        {
        	"editorversion" "400"
        	"editorbuild" "8873"
        	"mapversion" "246"
        	"formatversion" "100"
        	"prefab" "0"
        }
        visgroups
        {
        }
        """;

    [Fact]
    public void ReadsMapVersionAndEditorBuild()
    {
        var info = VmfVersionInfo.Parse(Head);
        Assert.Equal(246, info.MapVersion);
        Assert.Equal(400, info.EditorVersion);
        Assert.Equal(8873, info.EditorBuild);
    }

    [Fact]
    public void AcceptsCrLf() => Assert.Equal(246, VmfVersionInfo.Parse(Head.ReplaceLineEndings("\r\n")).MapVersion);

    [Fact]
    public void ThrowsWithoutVersionInfo() => Assert.Throws<InvalidDataException>(() => VmfVersionInfo.Parse("world\n{\n}\n"));
}
