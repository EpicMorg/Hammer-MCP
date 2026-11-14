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

    public enum State { Pending, Applied, Rejected, Failed }

    /// <summary>One change: plain-language text for the user, the action that does it, and what happened.</summary>
    public sealed class Change
    {
        internal Change(string id, string text, Func<(bool Ok, string Result)> action)
        {
            Id = id;
            Text = text;
            Action = action;
        }

        public string Id { get; }
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
    private int _next;
    private bool _ask;

    /// <summary>The queue changed (a change arrived or was decided) or the mode changed. Raised outside the lock.</summary>
    public event Action? Changed;

    /// <summary>A new change waits for the user (the app pops up its list).</summary>
    public event Action<Change>? Arrived;

    /// <summary>A change was applied, rejected or failed (for the activity log).</summary>
    public event Action<Change>? Decided;

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
            // the action keeps its typed result for the waiting tool call; the queue itself only needs ok + text
            typed = new Change($"c{++_next}", text, () =>
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
            _ => refused($"the user rejected this change ({typed.Id}: {text}) - do not repeat it; ask them what they want instead"),
        };
    }

    /// <summary>The user's decision: apply some ids (in the order they were asked), reject others.</summary>
    public void Decide(IEnumerable<string> apply, IEnumerable<string> reject)
    {
        var applyIds = apply.ToHashSet();
        var rejectIds = reject.ToHashSet();
        List<Change> toApply, toReject;
        lock (_lock)
        {
            toApply = _pending.Where(c => applyIds.Contains(c.Id)).ToList();
            toReject = _pending.Where(c => rejectIds.Contains(c.Id) && !applyIds.Contains(c.Id)).ToList();
            foreach (var c in toApply.Concat(toReject))
                _pending.Remove(c);
        }
        foreach (var c in toReject)
        {
            c.State = State.Rejected;
            c.Result = "rejected by the user";
            Finish(c);
        }
        if (toReject.Count > 0)
            Changed?.Invoke();
        if (toApply.Count == 0)
            return;
        // never on the caller's thread (the app's UI thread, the adapter's pipe thread): a compile takes minutes.
        // One after another, in the order the agent asked for them.
        _ = Task.Run(() =>
        {
            foreach (var c in toApply)
            {
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
            }
            Changed?.Invoke();
        });
    }

    public void RejectAll() => Decide([], Pending().Select(c => c.Id));

    public void ApplyAll() => Decide(Pending().Select(c => c.Id), []);

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
