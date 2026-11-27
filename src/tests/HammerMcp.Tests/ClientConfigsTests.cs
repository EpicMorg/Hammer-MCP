using System.Text.Json;
using HammerMcp.Server;

namespace HammerMcp.Tests;

public class ClientConfigsTests
{
    public static TheoryData<bool> WithToken => [true, false];

    [Theory]
    [MemberData(nameof(WithToken))]
    public void EveryConfigHasTheUrlAndTheTokenOnlyWhenSet(bool token)
    {
        var o = new HammerMcpServerOptions { Port = 47800, Token = token ? "tok_123" : "" };
        foreach (var (client, text) in ClientConfigs.All)
        {
            var s = text(o);
            Assert.Contains("http://127.0.0.1:47800/mcp", s);
            Assert.Equal(token, s.Contains("Bearer tok_123"));
            if (s.TrimStart().StartsWith('{'))
                JsonDocument.Parse(s).Dispose(); // valid JSON for the JSON clients
            Assert.DoesNotContain("\r", s);
            Assert.False(string.IsNullOrWhiteSpace(client));
        }
    }

    [Fact]
    public void CodexIsToml()
    {
        var codex = ClientConfigs.All.Single(c => c.Client.StartsWith("Codex")).Text(new HammerMcpServerOptions { Token = "t" });
        Assert.StartsWith("[mcp_servers.hammer]\nurl = \"http://127.0.0.1:47800/mcp\"\n", codex);
        Assert.Contains("http_headers = { \"Authorization\" = \"Bearer t\" }", codex);
    }

    [Fact]
    public void OhMyPiUsesTheNativeHttpServerShape()
    {
        var config = ClientConfigs.All.Single(c => c.Client.StartsWith("Oh My Pi")).Text(
            new HammerMcpServerOptions { Port = 47801, Token = "local_token" });
        using var doc = JsonDocument.Parse(config);
        var hammer = doc.RootElement.GetProperty("mcpServers").GetProperty("hammer");
        Assert.Equal("http", hammer.GetProperty("type").GetString());
        Assert.Equal("http://127.0.0.1:47801/mcp", hammer.GetProperty("url").GetString());
        Assert.Equal("Bearer local_token", hammer.GetProperty("headers").GetProperty("Authorization").GetString());
    }
}
