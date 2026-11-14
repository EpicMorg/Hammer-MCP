namespace HammerMcp.Server;

/// <summary>
/// Approval mode ("agent edits: ask me"): an edit from the agent is not run right away but waits here until the user
/// applies or rejects it (in the app, or in Hammer's MCP > Pending changes). The tool call waits up to
/// <see cref="Wait"/>; if the user has not decided by then, the agent is told so and learns the outcome later from
/// pending_changes. Pausing the agent rejects everything that is waiting.
/// </summary>
public sealed class PendingChanges
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(2);
    private const int KeepDecided = 50;

    /// <summary>More changes than this waiting for the user = the agent is told to wait (each waiting call holds a thread).</summary>
    public const int MaxWaiting = 20;

    public enum State { Pending, Applied, Rejected, Failed }

    /// <summary>One change: plain-language text for the user, the action that does it, and what happened.</summary>
    public sealed class Change
    {
        internal Change(int number, string text, Func<(bool Ok, string Result)> action)
        {
            Number = number;
            Id = $"c{number}";
            Text = text;
            Action = action;
        }

        public string Id { get; }
        internal int Number { get; }
        public string Text { get; }
        public DateTime Asked { get; } = DateTime.Now;
        public State State { get; internal set; }
        public string? Result { get; internal set; }
        internal Func<(bool Ok, string Result)> Action { get; }
        internal ManualResetEventSlim Decided { get; } = new(false);
        internal object Value { get; set; } = null!;
    }

    private readonly object _lock = new();
    private readonly List<Change> _pending = [];
    private readonly Queue<Change> _decided = new();
    private readonly Queue<Change> _toApply = new();
    private bool _applying;
    private int _next;
    private bool _ask;

    /// <summary>The queue changed (a change arrived or was decided) or the mode changed. Raised outside the lock.</summary>
    public event Action? Changed;

    /// <summary>A new change waits for the user (the app pops up its list).</summary>
    public event Action<Change>? Arrived;

    /// <summary>A change was applied, rejected or failed (for the activity log).</summary>
    public event Action<Change>? Decided;

    /// <summary>Non-null = nothing may be applied now, with this reason (the agent is paused); checked before each change.</summary>
    public Func<string?>? Blocked { get; set; }

    public bool AskUser
    {
        get { lock (_lock) return _ask; }
        set
        {
            lock (_lock)
            {
                if (_ask == value)
                    return;
                _ask = value;
            }
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<Change> Pending()
    {
        lock (_lock)
            return [.. _pending];
    }

    public IReadOnlyList<Change> RecentlyDecided()
    {
        lock (_lock)
            return [.. _decided];
    }

    public int Count
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>
    /// Runs <paramref name="action"/> now (approval off) or after the user applies it. <paramref name="refused"/> makes
    /// the tool's result when it is rejected or still waiting.
    /// </summary>
    public T Run<T>(string text, Func<T> action, Func<T, (bool Ok, string Result)> describe, Func<string, T> refused)
    {
        if (!AskUser)
            return action();
        Change typed = null!;
        lock (_lock)
        {
            if (_pending.Count >= MaxWaiting)
                return refused($"{_pending.Count} changes already wait for the user's approval - do not add more now; "
                               + "call pending_changes to see what they decided, then continue");
            // the action keeps its typed result for the waiting tool call; the queue itself only needs ok + text
            typed = new Change(++_next, text, () =>
            {
                var value = action();
                typed.Value = value!;
                return describe(value);
            });
            _pending.Add(typed);
        }
        Arrived?.Invoke(typed);
        Changed?.Invoke();

        if (!typed.Decided.Wait(Wait))
            return refused($"waiting for the user's approval ({typed.Id}: {text}) - they have not decided within {Wait.TotalMinutes:0} min; "
                           + "it stays in their list: call pending_changes later to learn whether it was applied, do not repeat it");
        return typed.State switch
        {
            State.Applied or State.Failed => (T)typed.Value,
            _ => refused($"{typed.Result ?? "rejected by the user"} ({typed.Id}: {text}) - do not repeat it; ask them what they want instead"),
        };
    }

    /// <summary>
    /// The user's decision: apply some ids, reject others. Applied changes go to one queue that a single worker runs
    /// in the order they were asked - never on the caller's thread (the app's UI thread, the adapter's pipe thread:
    /// a compile takes minutes) and never two at once, however many decisions come in.
    /// </summary>
    public void Decide(IEnumerable<string> apply, IEnumerable<string> reject, string rejectedBecause = "rejected by the user")
    {
        var applyIds = apply.ToHashSet();
        var rejectIds = reject.ToHashSet();
        List<Change> toReject;
        var startWorker = false;
        lock (_lock)
        {
            var toApply = _pending.Where(c => applyIds.Contains(c.Id)).ToList();
            toReject = _pending.Where(c => rejectIds.Contains(c.Id) && !applyIds.Contains(c.Id)).ToList();
            foreach (var c in toApply.Concat(toReject))
                _pending.Remove(c);
            foreach (var c in toApply.OrderBy(c => c.Number))
                _toApply.Enqueue(c);
            if (_toApply.Count > 0 && !_applying)
                startWorker = _applying = true;
        }
        foreach (var c in toReject)
            Reject(c, rejectedBecause);
        if (toReject.Count > 0)
            Changed?.Invoke();
        if (startWorker)
            _ = Task.Run(ApplyQueued);
    }

    private void ApplyQueued()
    {
        while (true)
        {
            Change c;
            lock (_lock)
            {
                if (!_toApply.TryDequeue(out c!))
                {
                    _applying = false;
                    break;
                }
            }
            if (Blocked?.Invoke() is { } blocked)
            {
                Reject(c, blocked);
                Changed?.Invoke();
                continue;
            }
            try
            {
                var (ok, result) = c.Action();
                c.State = ok ? State.Applied : State.Failed;
                c.Result = result;
            }
            catch (Exception e)
            {
                c.State = State.Failed;
                c.Result = e.Message;
            }
            Finish(c);
            Changed?.Invoke();
        }
    }

    public void RejectAll(string because = "rejected by the user") => Decide([], Pending().Select(c => c.Id), because);

    public void ApplyAll() => Decide(Pending().Select(c => c.Id), []);

    /// <summary>
    /// Pause: rejects everything still waiting and everything applied but not started yet (a change that is running
    /// right now finishes; its own action checks the pause again).
    /// </summary>
    public void Stop(string because)
    {
        List<Change> queued;
        lock (_lock)
        {
            queued = [.. _toApply];
            _toApply.Clear();
        }
        foreach (var c in queued)
            Reject(c, because);
        RejectAll(because);
        if (queued.Count > 0)
            Changed?.Invoke();
    }

    private void Reject(Change c, string because)
    {
        c.State = State.Rejected;
        c.Result = because;
        Finish(c);
    }

    private void Finish(Change c)
    {
        lock (_lock)
        {
            _decided.Enqueue(c);
            while (_decided.Count > KeepDecided)
                _decided.Dequeue();
        }
        c.Decided.Set();
        Decided?.Invoke(c);
    }
}
