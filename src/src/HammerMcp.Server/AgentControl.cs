namespace HammerMcp.Server;

/// <summary>
/// What the user decided about the agent from Hammer's MCP menu (or the app): pause (the stop button: every edit from the
/// agent is refused), the chat with the agent ("Chat..."), and the recent activity shown in "Activity log...".
/// Lives in the server; the adapter only asks for changes.
/// </summary>
public sealed class AgentControl
{
    private const int MaxLines = 200;
    private readonly object _lock = new();
    private readonly Queue<string> _recent = new();
    private readonly List<(DateTime At, string Text)> _requests = [];
    private readonly Queue<ChatLine> _chat = new();
    private bool _paused;

    /// <summary>Pause or requests changed.</summary>
    public event Action? Changed;

    /// <summary>A new chat line (the user's or the agent's); raised outside the lock.</summary>
    public event Action<ChatLine>? Chat;

    /// <summary>A new activity line (for the live log in Hammer).</summary>
    public event Action<string>? Remembered;

    public bool Paused
    {
        get { lock (_lock) return _paused; }
        set
        {
            lock (_lock)
            {
                if (_paused == value)
                    return;
                _paused = value;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>Text for a refused edit while paused.</summary>
    public const string PausedReason =
        "the user paused the agent (MCP > Agent paused in Hammer): no edits until they resume - tell the user what you wanted to do and wait";

    /// <summary>Remembers an activity line for the adapter's "Activity log..." (the newest 200).</summary>
    public void Remember(string line)
    {
        lock (_lock)
        {
            _recent.Enqueue(line);
            while (_recent.Count > MaxLines)
                _recent.Dequeue();
        }
        Remembered?.Invoke(line);
    }

    public IReadOnlyList<string> Recent()
    {
        lock (_lock)
            return [.. _recent];
    }

    /// <summary>A line the user wrote for the agent (Hammer's MCP > Chat): queued for the agent and added to the chat.</summary>
    public void AddRequest(string text)
    {
        var line = new ChatLine("user", text, DateTime.Now);
        lock (_lock)
        {
            _requests.Add((line.At, text));
            AddChat(line);
        }
        Changed?.Invoke();
        Chat?.Invoke(line);
    }

    /// <summary>The agent's answer to the user (say_to_user).</summary>
    public ChatLine Say(string text)
    {
        var line = new ChatLine("agent", text, DateTime.Now);
        lock (_lock)
            AddChat(line);
        Chat?.Invoke(line);
        return line;
    }

    /// <summary>The chat so far (the newest 200 lines).</summary>
    public IReadOnlyList<ChatLine> ChatHistory()
    {
        lock (_lock)
            return [.. _chat];
    }

    private void AddChat(ChatLine line)
    {
        _chat.Enqueue(line);
        while (_chat.Count > MaxLines)
            _chat.Dequeue();
    }

    /// <summary>The user's requests not yet seen by the agent (and forgets them: each is delivered once).</summary>
    public IReadOnlyList<string> TakeRequests()
    {
        lock (_lock)
        {
            var all = _requests.Select(r => $"{r.At:HH:mm:ss} {r.Text}").ToList();
            _requests.Clear();
            return all;
        }
    }

    public int PendingRequests
    {
        get { lock (_lock) return _requests.Count; }
    }
}

/// <summary>One chat line: from "user" or "agent".</summary>
public sealed record ChatLine(string From, string Text, DateTime At);
