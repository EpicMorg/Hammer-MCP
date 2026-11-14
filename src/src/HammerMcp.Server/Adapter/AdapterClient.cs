using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HammerMcp.Server.Adapter;

/// <summary>What the adapter says about itself in its first line (<c>hello</c>).</summary>
public sealed record AdapterHello(string Adapter, string Version, int Pid, string Build, string EditorSha256, string Support = "menu");

/// <summary>
/// Client side of the adapter pipe <c>\\.\pipe\hammer-mcp-adapter-&lt;pid&gt;</c> (protocol: src/native/PROTOCOL.md).
/// One JSON object per line; messages to the adapter carry "cmd", from it "type". Heartbeat: ping every 5 s,
/// no answer for 15 s = the adapter is gone. Events are raised on a background thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdapterClient : IAsyncDisposable
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(15);

    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private long _lastHeardTicks = DateTime.UtcNow.Ticks; // written by the read loop, read by the ping loop
    private int _closed;

    public AdapterHello Hello { get; private set; } = null!;

    public bool Connected => _closed == 0 && _pipe.IsConnected;

    /// <summary>A menu item clicked in Hammer: item (pause, resume, pending, undo_agent_step, log, disconnect) and request id.</summary>
    public event Action<string, string?>? Menu;

    /// <summary>Text the user typed in MCP > Ask agent...</summary>
    public event Action<string>? Ask;

    /// <summary>The user's decision on pending changes: ids to apply, ids to reject.</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? Approval;

    /// <summary>The connection ended (bye after unload, Hammer closed, heartbeat lost); the text says why.</summary>
    public event Action<string>? Closed;

    private AdapterClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    }

    public static string PipeName(int pid) => $"hammer-mcp-adapter-{pid}";

    /// <summary>Connects to the adapter of this Hammer process and reads its hello.</summary>
    public static async Task<AdapterClient> ConnectAsync(int pid, TimeSpan timeout, CancellationToken cancel = default)
    {
        var pipe = new NamedPipeClientStream(".", PipeName(pid), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, cancel);
            var client = new AdapterClient(pipe);
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            helloTimeout.CancelAfter(timeout);
            var line = await client._reader.ReadLineAsync(helloTimeout.Token)
                       ?? throw new IOException("the adapter closed the pipe before saying hello");
            var hello = JsonNode.Parse(line)?.AsObject();
            if (Str(hello, "type") != "hello")
                throw new IOException($"unexpected first line from the adapter: {line}");
            client.Hello = new AdapterHello(Str(hello, "adapter") ?? "?", Str(hello, "version") ?? "?",
                hello?["pid"]?.GetValue<int>() ?? pid, Str(hello, "build") ?? "?", Str(hello, "editorSha256") ?? "",
                Str(hello, "support") ?? "menu");
            _ = Task.Run(client.ReadLoopAsync);
            _ = Task.Run(client.PingLoopAsync);
            return client;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    // ---- to the adapter ----------------------------------------------------------------------------------------

    public Task SendStateAsync(string server, string mode, bool paused, bool approvalAsk, int pending, bool canUndo) =>
        SendAsync(new JsonObject
        {
            ["cmd"] = "state", ["server"] = server, ["mode"] = mode, ["agent"] = paused ? "paused" : "active",
            ["approval"] = approvalAsk ? "ask" : "auto", ["pending"] = pending, ["canUndo"] = canUndo,
        });

    public Task SendNotifyAsync(string text) => SendAsync(new JsonObject { ["cmd"] = "notify", ["text"] = text });

    public Task SendLogAsync(string? id, IEnumerable<string> lines) =>
        SendAsync(new JsonObject { ["cmd"] = "log", ["id"] = id, ["lines"] = new JsonArray([.. lines.Select(l => JsonValue.Create(l))]) });

    public Task SendPendingAsync(string? id, IEnumerable<(string Id, string Text)> items) =>
        SendAsync(new JsonObject
        {
            ["cmd"] = "pending", ["id"] = id,
            ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject { ["id"] = i.Id, ["text"] = i.Text })]),
        });

    /// <summary>Asks the adapter to remove its menu and unload itself; it answers bye and the connection closes.</summary>
    public Task UnloadAsync() => SendAsync(new JsonObject { ["cmd"] = "unload" });

    private async Task SendAsync(JsonObject message)
    {
        if (!Connected)
            return;
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        await _writeLock.WaitAsync();
        try
        {
            await _pipe.WriteAsync(bytes);
            await _pipe.FlushAsync();
        }
        catch (IOException e)
        {
            Close($"write failed: {e.Message}");
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ---- from the adapter --------------------------------------------------------------------------------------

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(_stop.Token);
                if (line is null)
                {
                    Close("the adapter closed the connection");
                    return;
                }
                Volatile.Write(ref _lastHeardTicks, DateTime.UtcNow.Ticks);
                if (line.Length > 0)
                    Dispatch(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException e)
        {
            Close($"read failed: {e.Message}");
        }
    }

    private void Dispatch(string line)
    {
        JsonObject? m;
        try
        {
            m = JsonNode.Parse(line)?.AsObject();
        }
        catch (JsonException)
        {
            return; // not ours to fail on: ignore a garbled line
        }
        switch (Str(m, "type"))
        {
            case "pong":
                break;
            case "bye":
                Close("the adapter unloaded itself");
                break;
            case "menu":
                Menu?.Invoke(Str(m, "item") ?? "", Str(m, "id"));
                break;
            case "ask":
                if (Str(m, "text") is { Length: > 0 } text)
                    Ask?.Invoke(text);
                break;
            case "approval":
                Approval?.Invoke(Ids(m, "apply"), Ids(m, "reject"));
                break;
        }
    }

    private async Task PingLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(PingEvery, _stop.Token);
                if (DateTime.UtcNow.Ticks - Volatile.Read(ref _lastHeardTicks) > LostAfter.Ticks)
                {
                    Close("no answer from the adapter for 15 s");
                    return;
                }
                await SendAsync(new JsonObject { ["cmd"] = "ping" });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Close(string why)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _stop.Cancel();
        try
        {
            _pipe.Dispose();
        }
        catch (IOException)
        {
        }
        Closed?.Invoke(why);
    }

    /// <summary>Drops the connection (the adapter stays loaded and waits for the next client). Safe from any event handler.</summary>
    public ValueTask DisposeAsync()
    {
        Close("closed by the server");
        return ValueTask.CompletedTask;
    }

    private static string? Str(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string[] Ids(JsonObject? o, string key) =>
        o?[key] is JsonArray a ? [.. a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()] : [];
}
