using HammerMcp.Core.Agents;

namespace HammerMcp.Server;

/// <summary>
/// Wakes an agent that works in OpenCode: each line the user writes in Hammer's chat (MCP > Chat) goes into the
/// OpenCode session as a user message, unless the agent is already in wait_for_user (then that call returns it).
/// A delivered line is taken out of the user_requests queue, so the agent does not get it twice.
/// </summary>
public sealed class OpenCodeBridge : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly AgentControl _control;
    private readonly ActivityLog _log;
    private readonly SemaphoreSlim _order = new(1, 1);
    private OpenCodeClient? _client;

    public OpenCodeBridge(AgentControl control, ActivityLog log)
    {
        _control = control;
        _log = log;
        _control.Chat += OnChat;
    }

    /// <summary>Null = off.</summary>
    public OpenCodeOptions? Options
    {
        get;
        set
        {
            field = value;
            _client = value is null ? null : new OpenCodeClient(Http, value);
        }
    }

    /// <summary>The text the agent gets: the user's line and how to answer it.</summary>
    public static string Message(string text) =>
        $"[Hammer] The user wrote in Hammer's chat (MCP > Chat): {text}\n"
        + "(Answer with the hammer-mcp tool say_to_user - the user is looking at Hammer; then carry on.)";

    /// <summary>Checks the connection: which session a line would go to.</summary>
    public static async Task<string> TestAsync(OpenCodeOptions options, CancellationToken ct = default)
    {
        var client = new OpenCodeClient(Http, options);
        var api = await client.DetectAsync(ct);
        var session = await client.ResolveSessionAsync(ct);
        return $"OpenCode API {(int)api}: lines go to {Describe(session)}";
    }

    private static string Describe(OpenCodeSession s) =>
        $"{s.Id}{(s.Title is { Length: > 0 } t ? $" \"{t}\"" : "")}{(s.Directory is { } d ? $" in {d}" : "")}";

    private void OnChat(ChatLine line)
    {
        if (line.From != "user" || _client is not { } client)
            return;
        if (_control.AgentWaiting)
        {
            _log.Write("opencode: the agent is waiting in wait_for_user - the line goes there, not to OpenCode");
            return;
        }
        _ = DeliverAsync(client, line);
    }

    private async Task DeliverAsync(OpenCodeClient client, ChatLine line)
    {
        await _order.WaitAsync(); // lines arrive in the order the user wrote them
        try
        {
            var session = await client.PromptAsync(Message(line.Text));
            _control.Take(line);
            _log.Write($"opencode: sent to session {Describe(session)}");
        }
        catch (Exception e) // fire and forget: whatever went wrong is logged, never lost in an unobserved task
        {
            // the line stays in user_requests: the agent still gets it there
            _log.Write($"opencode: could not send the line ({e.Message}); it waits in user_requests");
        }
        finally
        {
            _order.Release();
        }
    }

    public void Dispose() => _control.Chat -= OnChat;
}
