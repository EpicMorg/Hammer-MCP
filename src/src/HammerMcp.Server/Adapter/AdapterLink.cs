using System.Runtime.Versioning;
using HammerMcp.Core.Registry;

namespace HammerMcp.Server.Adapter;

/// <summary>
/// The server's connection to the adapter in the attached Hammer: injects on the user's request, keeps the pipe,
/// answers Hammer's MCP menu (pause, ask, activity log, disconnect) and pushes the server state to it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdapterLink(HammerTarget target, ActivityLog log)
{
    private static readonly string ServerVersion =
        typeof(AdapterLink).Assembly.GetName().Version?.ToString(3) ?? "0";

    private AdapterClient? _client;

    /// <summary>Connected or disconnected (raised on a background thread).</summary>
    public event Action? Changed;

    /// <summary>MCP mode shown in Hammer's Status / About (set by the app).</summary>
    public McpMode Mode { get; set; } = McpMode.Auto;

    public AdapterHello? Hello => _client is { Connected: true } c ? c.Hello : null;

    public bool Connected => _client is { Connected: true };

    private AgentControl Control => target.Control;

    /// <summary>Injects the adapter into the attached Hammer (if it is not there yet) and connects to it.</summary>
    public async Task<string> ConnectAsync(HammerBuild? build, CancellationToken cancel = default)
    {
        if (Connected)
            return $"already connected to {Hello!.Adapter}";
        var process = target.Current() ?? throw new InvalidOperationException("no Hammer is attached");
        var injected = await AdapterInjector.InjectAsync(process.ProcessId, process.ExePath, build, cancel);
        log.Write($"adapter: {(injected.Ok ? injected.AlreadyLoaded ? "already loaded" : "injected" : "not injected")} - {injected.Message}");
        if (!injected.Ok)
            return injected.Message;

        // the adapter creates its pipe right after it loads; its menu appears once Hammer's main window exists
        var client = await AdapterClient.ConnectAsync(process.ProcessId, TimeSpan.FromSeconds(10), cancel);
        client.Menu += OnMenu;
        client.Ask += OnAsk;
        client.Approval += (apply, reject) => log.Write($"adapter: approval ignored (approval mode is not on): apply {apply.Count}, reject {reject.Count}");
        client.Closed += why =>
        {
            log.Write($"adapter disconnected: {why}");
            if (ReferenceEquals(_client, client))
                _client = null;
            Changed?.Invoke();
        };
        _client = client;
        log.Write($"adapter connected: {client.Hello.Adapter} {client.Hello.Version}, build {client.Hello.Build} ({client.Hello.EditorSha256[..Math.Min(12, client.Hello.EditorSha256.Length)]})");
        await PushStateAsync();
        Changed?.Invoke();
        return $"connected to {client.Hello.Adapter} {client.Hello.Version} ({client.Hello.Build})";
    }

    /// <summary>Asks the adapter to unload itself (its menu disappears; Hammer keeps running).</summary>
    public async Task DisconnectAsync()
    {
        if (_client is { } c)
            await c.UnloadAsync();
    }

    /// <summary>Sends the current state (pause, mode) to Hammer's MCP menu.</summary>
    public Task PushStateAsync() =>
        _client is { Connected: true } c
            ? c.SendStateAsync(ServerVersion, Mode.ToString(), Control.Paused, approvalAsk: false, pending: 0, canUndo: false)
            : Task.CompletedTask;

    public Task NotifyAsync(string text) =>
        _client is { Connected: true } c ? c.SendNotifyAsync(text) : Task.CompletedTask;

    // event handlers from the pipe thread: an exception must never escape (async void would take the whole app down)
    private async void OnMenu(string item, string? id)
    {
        try
        {
            await HandleMenuAsync(item, id);
        }
        catch (Exception e)
        {
            log.Write($"adapter: menu '{item}' failed: {e.Message}");
        }
    }

    private async void OnAsk(string text)
    {
        try
        {
            Control.AddRequest(text);
            log.Write($"user request from Hammer: {text}");
            await NotifyAsync("request queued: the agent sees it with its next hammer_status / user_requests call");
        }
        catch (Exception e)
        {
            log.Write($"adapter: ask failed: {e.Message}");
        }
    }

    private async Task HandleMenuAsync(string item, string? id)
    {
        var c = _client;
        if (c is null)
            return;
        switch (item)
        {
            case "pause" or "resume":
                Control.Paused = item == "pause";
                log.Write(Control.Paused ? "agent paused by the user (Hammer: MCP > Agent paused)" : "agent resumed by the user (Hammer: MCP menu)");
                await PushStateAsync();
                break;
            case "log":
                await c.SendLogAsync(id, Control.Recent());
                break;
            case "pending":
                await c.SendPendingAsync(id, []); // the approval mode comes later: nothing is ever pending yet
                break;
            case "undo_agent_step":
                await c.SendNotifyAsync("undo of agent steps is not available yet - use Ctrl+Z in Hammer");
                break;
            case "disconnect":
                log.Write("adapter: disconnect requested in Hammer");
                await c.UnloadAsync();
                break;
        }
    }
}
