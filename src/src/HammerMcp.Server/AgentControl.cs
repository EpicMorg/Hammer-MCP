namespace HammerMcp.Server;

/// <summary>
/// What the user decided about the agent from Hammer's MCP menu (or the app): pause (the stop button: every edit from the
/// agent is refused), requests typed in "Ask agent...", and the recent activity shown in "Activity log...".
/// Lives in the server; the adapter only asks for changes.
/// </summary>
public sealed class AgentControl
{
    private const int MaxLines = 200;
    private readonly object _lock = new();
    private readonly Queue<string> _recent = new();
    private readonly List<(DateTime At, string Text)> _requests = [];
    private bool _paused;

    /// <summary>Pause, requests or activity changed.</summary>
    public event Action? Changed;

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
    }

    public IReadOnlyList<string> Recent()
    {
        lock (_lock)
            return [.. _recent];
    }

    /// <summary>A request the user typed in Hammer for the agent.</summary>
    public void AddRequest(string text)
    {
        lock (_lock)
            _requests.Add((DateTime.Now, text));
        Changed?.Invoke();
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
