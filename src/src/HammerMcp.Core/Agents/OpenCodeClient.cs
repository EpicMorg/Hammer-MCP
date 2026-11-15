using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HammerMcp.Core.Agents;

/// <summary>Where the OpenCode server is: its URL (opencode serve / the TUI's server), Basic auth and the session to write to.</summary>
public sealed record OpenCodeOptions
{
    /// <summary>Base URL, e.g. http://127.0.0.1:4096 (opencode serve --port 4096).</summary>
    public string Url { get; init; } = "http://127.0.0.1:4096";

    /// <summary>Basic auth user: OPENCODE_SERVER_USERNAME, "opencode" by default.</summary>
    public string Username { get; init; } = "opencode";

    /// <summary>Basic auth password: OPENCODE_SERVER_PASSWORD, or the one opencode serve prints; empty = no auth.</summary>
    public string Password { get; init; } = "";

    /// <summary>Session id (ses_...); empty = the most recently used top-level session.</summary>
    public string Session { get; init; } = "";

    /// <summary>
    /// Why the password would be exposed: Basic auth over plain http to another machine sends it base64-encoded but
    /// unencrypted. Null when there is no password, the URL is https or it points to this machine.
    /// </summary>
    public string? PasswordExposure()
    {
        if (Password.Length == 0 || !Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback)
            return null;
        return $"the password is sent unencrypted to a non-local address ({uri.Host}) over http - use https, or OpenCode on this machine";
    }
}

/// <summary>A session the message can go to.</summary>
public sealed record OpenCodeSession(string Id, string? Title, string? Directory, DateTimeOffset Used);

/// <summary>
/// Puts a user message into a running OpenCode session over its HTTP API, so the agent there wakes up. OpenCode 2.x:
/// GET /api/session, POST /api/session/{id}/prompt {text, delivery: "queue"} (after the current turn if busy).
/// OpenCode 1.x: GET /session, POST /session/{id}/prompt_async {parts: [{type: "text", text}]} (204).
/// </summary>
public sealed class OpenCodeClient(HttpClient http, OpenCodeOptions options)
{
    public enum ApiVersion { V1 = 1, V2 = 2 }

    private ApiVersion? _api;

    /// <summary>The API the server speaks (asked once: /api/session answers JSON on 2.x, the web app's HTML on 1.x).</summary>
    public async Task<ApiVersion> DetectAsync(CancellationToken ct = default)
    {
        if (_api is { } known)
            return known;
        using var response = await SendAsync(HttpMethod.Get, "/api/session?limit=1", null, ct);
        if (IsJson(response))
        {
            // 2.x answers JSON here, errors too (401 = wrong password: report it, it is not a sign of 1.x)
            await EnsureOkAsync(response, "GET /api/session", ct);
            return (_api = ApiVersion.V2).Value;
        }
        using var v1 = await SendAsync(HttpMethod.Get, "/session", null, ct);
        await EnsureOkAsync(v1, "GET /session", ct);
        if (!IsJson(v1))
            throw new OpenCodeException($"{options.Url} does not look like an OpenCode server (no JSON from /api/session or /session)");
        return (_api = ApiVersion.V1).Value;
    }

    /// <summary>Top-level sessions (no parent, not archived), most recently used first.</summary>
    public async Task<IReadOnlyList<OpenCodeSession>> SessionsAsync(CancellationToken ct = default)
    {
        var api = await DetectAsync(ct);
        var path = api == ApiVersion.V2 ? "/api/session?limit=100" : "/session";
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        await EnsureOkAsync(response, $"GET {path}", ct);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var list = (api == ApiVersion.V2 ? root?["data"] : root) as JsonArray ?? [];
        return ParseSessions(list);
    }

    internal static List<OpenCodeSession> ParseSessions(JsonArray list)
    {
        var sessions = new List<OpenCodeSession>();
        foreach (var s in list.OfType<JsonObject>())
        {
            if (s["id"]?.GetValueKind() != JsonValueKind.String || s["parentID"] is JsonValue)
                continue;
            var time = s["time"] as JsonObject;
            if (time?["archived"] is JsonValue)
                continue;
            // the TUI marks a session viewed when it is shown: the newest of updated / viewed is the one in use
            var used = new[] { Number(time?["updated"]), Number(time?["viewed"]), Number(time?["created"]) }.Max();
            var directory = s["location"]?["directory"] ?? s["directory"];
            sessions.Add(new OpenCodeSession(
                s["id"]!.GetValue<string>(),
                s["title"]?.GetValueKind() == JsonValueKind.String ? s["title"]!.GetValue<string>() : null,
                directory?.GetValueKind() == JsonValueKind.String ? directory.GetValue<string>() : null,
                DateTimeOffset.FromUnixTimeMilliseconds((long)used)));
        }
        return [.. sessions.OrderByDescending(s => s.Used)];
    }

    private static double Number(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<double>() : 0;

    /// <summary>The session from the options, or the most recently used one.</summary>
    public async Task<OpenCodeSession> ResolveSessionAsync(CancellationToken ct = default)
    {
        var sessions = await SessionsAsync(ct);
        if (options.Session.Trim() is { Length: > 0 } id)
            return sessions.FirstOrDefault(s => s.Id == id) ?? new OpenCodeSession(id, null, null, default);
        return sessions.FirstOrDefault()
               ?? throw new OpenCodeException("OpenCode has no sessions yet: open one in OpenCode first (or set the session id)");
    }

    /// <summary>Sends <paramref name="text"/> as a user message; the agent answers it after its current turn (if busy).</summary>
    public async Task<OpenCodeSession> PromptAsync(string text, CancellationToken ct = default)
    {
        var session = await ResolveSessionAsync(ct);
        var api = await DetectAsync(ct);
        var id = Uri.EscapeDataString(session.Id);
        // JSON nodes, not anonymous types: the app runs without reflection-based serialization
        var (path, body) = api == ApiVersion.V2
            ? ($"/api/session/{id}/prompt", new JsonObject { ["text"] = text, ["delivery"] = "queue" })
            : ($"/session/{id}/prompt_async", new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) });
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, path, content, ct);
        await EnsureOkAsync(response, $"POST {path}", ct);
        return session;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, options.Url.TrimEnd('/') + path) { Content = content };
        if (options.Password.Length > 0)
        {
            var user = string.IsNullOrWhiteSpace(options.Username) ? "opencode" : options.Username.Trim();
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{options.Password}")));
        }
        return http.SendAsync(request, ct);
    }

    private static bool IsJson(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task EnsureOkAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(ct);
        string? message = null;
        try
        {
            message = JsonNode.Parse(body)?["message"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
        }
        var hint = response.StatusCode == HttpStatusCode.Unauthorized
            ? " - wrong or missing password (OPENCODE_SERVER_PASSWORD, or the one opencode serve printed)"
            : "";
        throw new OpenCodeException($"{what}: {(int)response.StatusCode} {message ?? response.ReasonPhrase}{hint}");
    }
}

public sealed class OpenCodeException(string message) : Exception(message);
