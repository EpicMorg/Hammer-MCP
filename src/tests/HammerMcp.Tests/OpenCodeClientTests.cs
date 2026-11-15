using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HammerMcp.Core.Agents;

namespace HammerMcp.Tests;

public class OpenCodeClientTests
{
    /// <summary>A fake OpenCode server: answers by "METHOD path" and records the requests.</summary>
    private sealed class FakeServer(Dictionary<string, (HttpStatusCode Code, string Type, string Body)> routes) : HttpMessageHandler
    {
        public List<(string Route, string? Auth, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var route = $"{request.Method} {request.RequestUri!.PathAndQuery}";
            Requests.Add((route, request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            // anything unknown gets the web app's HTML, as OpenCode does
            var (code, type, body) = routes.TryGetValue(route, out var r) ? r : (HttpStatusCode.OK, "text/html", "<!doctype html>");
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, type) };
        }
    }

    private const string Json = "application/json";

    private const string V2Sessions = """
        {"data":[
          {"id":"ses_old","title":"old","time":{"created":1000,"updated":2000},"location":{"directory":"C:\\a"}},
          {"id":"ses_viewed","title":"viewed","time":{"created":1000,"updated":3000,"viewed":9000},"location":{"directory":"C:\\b"}},
          {"id":"ses_child","parentID":"ses_old","time":{"created":1000,"updated":99999}},
          {"id":"ses_archived","time":{"created":1000,"updated":99999,"archived":99999}}
        ],"cursor":{}}
        """;

    [Fact]
    public async Task V2PromptGoesToTheMostRecentlyUsedTopLevelSession()
    {
        var server = new FakeServer(new()
        {
            ["GET /api/session?limit=1"] = (HttpStatusCode.OK, Json, V2Sessions),
            ["GET /api/session?limit=100"] = (HttpStatusCode.OK, Json, V2Sessions),
            ["POST /api/session/ses_viewed/prompt"] = (HttpStatusCode.OK, Json, """{"data":{"id":"msg_1"}}"""),
        });
        var client = new OpenCodeClient(new HttpClient(server), new OpenCodeOptions { Url = "http://oc:4096/", Password = "pw" });

        var session = await client.PromptAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal("ses_viewed", session.Id);
        Assert.Equal(@"C:\b", session.Directory);
        var post = server.Requests.Single(r => r.Route.StartsWith("POST"));
        var body = JsonNode.Parse(post.Body!)!;
        Assert.Equal("hello", body["text"]!.GetValue<string>());
        Assert.Equal("queue", body["delivery"]!.GetValue<string>());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:pw")), post.Auth);
    }

    [Fact]
    public async Task V1UsesPromptAsyncWithATextPart()
    {
        var server = new FakeServer(new()
        {
            ["GET /session"] = (HttpStatusCode.OK, Json, """[{"id":"ses_a","time":{"created":1,"updated":5}},{"id":"ses_b","time":{"created":1,"updated":7}}]"""),
            ["POST /session/ses_b/prompt_async"] = (HttpStatusCode.NoContent, Json, ""),
        });
        var client = new OpenCodeClient(new HttpClient(server), new OpenCodeOptions { Url = "http://oc:4096" });

        Assert.Equal(OpenCodeClient.ApiVersion.V1, await client.DetectAsync(TestContext.Current.CancellationToken));
        var session = await client.PromptAsync("hi", TestContext.Current.CancellationToken);

        Assert.Equal("ses_b", session.Id);
        var post = server.Requests.Single(r => r.Route.StartsWith("POST"));
        Assert.Null(post.Auth); // no password: no auth header
        var part = JsonNode.Parse(post.Body!)!["parts"]![0]!;
        Assert.Equal("text", part["type"]!.GetValue<string>());
        Assert.Equal("hi", part["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task AConfiguredSessionWinsOverTheRecentOne()
    {
        var server = new FakeServer(new()
        {
            ["GET /api/session?limit=1"] = (HttpStatusCode.OK, Json, V2Sessions),
            ["GET /api/session?limit=100"] = (HttpStatusCode.OK, Json, V2Sessions),
            ["POST /api/session/ses_old/prompt"] = (HttpStatusCode.OK, Json, "{}"),
        });
        var client = new OpenCodeClient(new HttpClient(server), new OpenCodeOptions { Session = " ses_old " });

        Assert.Equal("old", (await client.PromptAsync("x", TestContext.Current.CancellationToken)).Title);
    }

    [Fact]
    public async Task UnauthorizedSaysWhatIsWrong()
    {
        var server = new FakeServer(new()
        {
            ["GET /api/session?limit=1"] = (HttpStatusCode.Unauthorized, Json, """{"_tag":"UnauthorizedError","message":"Authentication required"}"""),
            ["GET /session"] = (HttpStatusCode.Unauthorized, Json, """{"_tag":"UnauthorizedError","message":"Authentication required"}"""),
        });
        var client = new OpenCodeClient(new HttpClient(server), new OpenCodeOptions());

        var e = await Assert.ThrowsAsync<OpenCodeException>(() => client.PromptAsync("x", TestContext.Current.CancellationToken));
        Assert.Contains("401 Authentication required", e.Message);
        Assert.Contains("password", e.Message);
    }

    [Theory]
    [InlineData("http://127.0.0.1:4096", "pw", false)]
    [InlineData("http://localhost:4096", "pw", false)]
    [InlineData("https://oc.example:4096", "pw", false)]
    [InlineData("http://192.168.1.5:4096", "", false)]
    [InlineData("http://192.168.1.5:4096", "pw", true)]
    [InlineData("http://oc.lan:4096", "pw", true)]
    public void PasswordOverPlainHttpToAnotherMachineIsFlagged(string url, string password, bool exposed) =>
        Assert.Equal(exposed, new OpenCodeOptions { Url = url, Password = password }.PasswordExposure() is not null);

    [Fact]
    public async Task NotOpenCodeIsReported()
    {
        var client = new OpenCodeClient(new HttpClient(new FakeServer([])), new OpenCodeOptions());

        var e = await Assert.ThrowsAsync<OpenCodeException>(() => client.DetectAsync(TestContext.Current.CancellationToken));
        Assert.Contains("does not look like an OpenCode server", e.Message);
    }

    [Fact]
    public async Task NoSessionsIsReported()
    {
        var server = new FakeServer(new()
        {
            ["GET /api/session?limit=1"] = (HttpStatusCode.OK, Json, """{"data":[],"cursor":{}}"""),
            ["GET /api/session?limit=100"] = (HttpStatusCode.OK, Json, """{"data":[],"cursor":{}}"""),
        });
        var client = new OpenCodeClient(new HttpClient(server), new OpenCodeOptions());

        var e = await Assert.ThrowsAsync<OpenCodeException>(() => client.PromptAsync("x", TestContext.Current.CancellationToken));
        Assert.Contains("no sessions", e.Message);
    }
}
