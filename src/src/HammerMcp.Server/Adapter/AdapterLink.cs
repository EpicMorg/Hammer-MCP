using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using HammerMcp.Core.Registry;

namespace HammerMcp.Server.Adapter;

/// <summary>
/// The server's connection to the adapter in the attached Hammer: injects on the user's request (or connects to an adapter
/// that is already loaded), keeps the pipe, answers Hammer's MCP menu (pause, chat, activity log, pending changes,
/// disconnect) and pushes the server state, the build profile, chat lines and the live log to it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdapterLink
{
    private static readonly string ServerVersion =
        typeof(AdapterLink).Assembly.GetName().Version?.ToString(3) ?? "0";

    private readonly HammerTarget target;
    private readonly ActivityLog log;
    private readonly HammerRegistry registry;
    private AdapterClient? _client;
    private volatile bool _logLive; // the user opened Activity log in Hammer: push every new line

    public AdapterLink(HammerTarget target, ActivityLog log, HammerRegistry registry)
    {
        this.target = target;
        this.log = log;
        this.registry = registry;
        target.Control.Chat += line => _ = Send(c => c.SendChatAsync(line));
        target.Control.Remembered += line =>
        {
            if (_logLive)
                _ = Send(c => c.SendLogLineAsync(line));
        };
    }

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
        return await OpenAsync(process.ProcessId, TimeSpan.FromSeconds(10), cancel);
    }

    /// <summary>
    /// Connects to an adapter that is already loaded in the attached Hammer (e.g. after the app was restarted) - never
    /// injects. Null if there is none.
    /// </summary>
    public async Task<string?> ConnectExistingAsync(CancellationToken cancel = default)
    {
        if (Connected || target.AttachedPid is not { } pid || !File.Exists($@"\\.\pipe\{AdapterClient.PipeName(pid)}"))
            return null;
        try
        {
            return await OpenAsync(pid, TimeSpan.FromSeconds(3), cancel);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            log.Write($"adapter: found a loaded adapter but could not connect: {e.Message}");
            return null;
        }
    }

    private async Task<string> OpenAsync(int pid, TimeSpan timeout, CancellationToken cancel)
    {
        var client = await AdapterClient.ConnectAsync(pid, timeout, cancel);
        client.Menu += OnMenu;
        client.Chat += OnChat;
        client.SelectionSent += OnSelectionSent;
        client.Approval += (apply, reject) =>
        {
            log.Write($"user decided in Hammer: apply {string.Join(", ", apply)}{(reject.Count > 0 ? $"; reject {string.Join(", ", reject)}" : "")}");
            target.Changes.Decide(apply, reject);
        };
        client.Closed += why =>
        {
            log.Write($"adapter disconnected: {why}");
            if (ReferenceEquals(_client, client))
            {
                _client = null;
                _logLive = false;
            }
            Changed?.Invoke();
        };
        _logLive = false;
        _client = client;
        var hello = client.Hello;
        log.Write($"adapter connected: {hello.Adapter} {hello.Version}, build {hello.Build} ({hello.EditorSha256[..Math.Min(12, hello.EditorSha256.Length)]})");
        // the profile from this server's registry (registry/hammers.json next to the app overrides the built-in one)
        var known = registry.Builds.FirstOrDefault(b => b.Id == hello.Build);
        await client.SendProfileAsync(hello.Build, known?.InjectAdapter ?? hello.Support, known?.Memory);
        await PushStateAsync();
        Changed?.Invoke();
        return $"connected to {hello.Adapter} {hello.Version} ({hello.Build})";
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
            ? c.SendStateAsync(ServerVersion, Mode.ToString(), Control.Paused, target.Changes.AskUser, target.Changes.Count, canUndo: false)
            : Task.CompletedTask;

    public Task NotifyAsync(string text) => Send(c => c.SendNotifyAsync(text));

    private HammerBuild? ConnectedBuild => _client is { Connected: true } c
        ? registry.Builds.FirstOrDefault(b => b.Id == c.Hello.Build)
        : null;

    /// <summary>The connected build has a live selection layout.</summary>
    public bool CanReadLive => ConnectedBuild is { Memory.Count: > 0 };

    /// <summary>The connected build has a live camera layout.</summary>
    public bool CanReadCamera => ConnectedBuild?.Memory.ContainsKey("doc.views") == true;

    /// <summary>The selection the user last sent with MCP > Send selection to agent (null = never), and when.</summary>
    public (JsonObject Selection, DateTime At)? SentSelection { get; private set; }

    /// <summary>Layer 2: asks the adapter for get_selection / get_camera and returns its answer (type selection / camera / error).</summary>
    public async Task<JsonObject> ReadLiveAsync(string command, CancellationToken cancel = default)
    {
        if (_client is not { Connected: true } c)
            throw new InvalidOperationException("the adapter is not connected (Advanced mode: Connect adapter)");
        if (!CanReadLive)
            throw new InvalidOperationException($"live reading is not known for build {c.Hello.Build} yet");
        return await c.RequestAsync(new JsonObject { ["cmd"] = command }, TimeSpan.FromSeconds(5), cancel);
    }

    private void OnSelectionSent(JsonObject selection)
    {
        try
        {
            SentSelection = (selection, DateTime.Now);
            var count = selection["items"] is JsonArray items ? items.Count : 0;
            var total = selection["count"]?.GetValue<int>() ?? count;
            Control.AddRequest($"(Hammer: the user sent you the selection - {total} object(s); see hammer_selection with sent=true)");
            log.Write($"user sent the selection from Hammer: {total} object(s)");
        }
        catch (Exception e)
        {
            log.Write($"adapter: selection failed: {e.Message}");
        }
    }

    private Task Send(Func<AdapterClient, Task> send)
    {
        try
        {
            return _client is { Connected: true } c ? send(c) : Task.CompletedTask;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return Task.CompletedTask;
        }
    }

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

    private void OnChat(string text)
    {
        try
        {
            // the line is echoed to Hammer's chat by the Control.Chat handler; the agent gets it from user_requests
            Control.AddRequest(text);
            log.Write($"user wrote in Hammer: {text}");
        }
        catch (Exception e)
        {
            log.Write($"adapter: chat failed: {e.Message}");
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
                _logLive = true; // from now on every new line goes to Hammer's log window
                break;
            case "chat_history":
                await c.SendChatHistoryAsync(id, Control.ChatHistory());
                break;
            case "pending":
                await c.SendPendingAsync(id, target.Changes.Pending().Select(p => (p.Id, p.Text)));
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
