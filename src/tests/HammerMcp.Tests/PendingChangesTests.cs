using HammerMcp.Server;

namespace HammerMcp.Tests;

public class PendingChangesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Starts a tool call that waits for approval; returns when the change is in the list.</summary>
    private static Task<string> Ask(PendingChanges changes, string text, Func<string> action)
    {
        var before = changes.Count;
        var call = Task.Run(() => changes.Run(text, action, r => (true, r), reason => "refused: " + reason));
        SpinWait.SpinUntil(() => changes.Count > before, Timeout);
        return call;
    }

    [Fact]
    public void RunsRightAwayWhenApprovalIsOff()
    {
        var changes = new PendingChanges();
        Assert.Equal("done", changes.Run("x", () => "done", r => (true, r), reason => reason));
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public async Task AppliesInOrderOneAtATimeAcrossDecisions()
    {
        var changes = new PendingChanges { AskUser = true };
        var order = new List<string>();
        var running = 0;
        var overlap = false;
        Func<string> Step(string name) => () =>
        {
            if (Interlocked.Increment(ref running) > 1)
                overlap = true;
            Thread.Sleep(100);
            lock (order)
                order.Add(name);
            Interlocked.Decrement(ref running);
            return name;
        };
        var a = Ask(changes, "a", Step("a"));
        var b = Ask(changes, "b", Step("b"));
        var c = Ask(changes, "c", Step("c"));
        var ids = changes.Pending().Select(p => p.Id).ToArray();

        changes.Decide([ids[0]], []);   // two quick decisions: must not run in parallel
        changes.ApplyAll();

        Assert.Equal(["a", "b", "c"], await Task.WhenAll(a, b, c).WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(["a", "b", "c"], order);
        Assert.False(overlap);
        Assert.All(changes.RecentlyDecided(), d => Assert.Equal(PendingChanges.State.Applied, d.State));
    }

    [Fact]
    public async Task StopRejectsWaitingAndQueuedWithTheReason()
    {
        var changes = new PendingChanges { AskUser = true };
        using var gate = new ManualResetEventSlim(false);
        var ranSecond = false;
        var first = Ask(changes, "first", () => { gate.Wait(Timeout); return "first"; });
        var second = Ask(changes, "second", () => { ranSecond = true; return "second"; });
        var third = Ask(changes, "third", () => "third");
        var ids = changes.Pending().Select(p => p.Id).ToArray();

        changes.Decide([ids[0], ids[1]], []);     // first runs (held by the gate), second is queued
        SpinWait.SpinUntil(() => changes.Count == 1, Timeout);
        changes.Stop("rejected: paused");
        gate.Set();

        Assert.Equal("first", await first.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.StartsWith("refused: rejected: paused", await second.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.StartsWith("refused: rejected: paused", await third.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.False(ranSecond);
        Assert.Equal(2, changes.RecentlyDecided().Count(d => d.State == PendingChanges.State.Rejected));
    }

    [Fact]
    public async Task BlockedRejectsInsteadOfFailing()
    {
        string? blocked = null;
        var changes = new PendingChanges { AskUser = true, Blocked = () => blocked };
        var call = Ask(changes, "x", () => "ran");
        blocked = "rejected: paused";
        changes.ApplyAll();
        Assert.StartsWith("refused: rejected: paused", await call.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(PendingChanges.State.Rejected, changes.RecentlyDecided().Single().State);
    }

    [Fact]
    public async Task RefusesMoreThanMaxWaiting()
    {
        var changes = new PendingChanges { AskUser = true };
        var calls = new List<Task<string>>();
        for (var i = 0; i < PendingChanges.MaxWaiting; i++)
            calls.Add(Ask(changes, $"c{i}", () => "ok"));
        Assert.StartsWith("refused:", changes.Run("one too many", () => "ok", r => (true, r), reason => "refused: " + reason));
        changes.RejectAll();
        await Task.WhenAll(calls).WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }
}
