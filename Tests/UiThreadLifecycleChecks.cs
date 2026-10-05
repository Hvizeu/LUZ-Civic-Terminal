using System.Collections.Concurrent;
using Luz;

internal static class UiThreadLifecycleChecks
{
    private sealed class UiContext : SynchronizationContext
    {
        internal readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => Pending.Enqueue((callback, state));
    }

    private static bool WithoutPumping(Action action)
    {
        var context = new UiContext(); Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { action(); } catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.Start();
        bool completed = thread.Join(1500);
        // Release a broken implementation after recording the deadlock, so fixtures can clean up.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (thread.IsAlive && DateTime.UtcNow < deadline)
        {
            while (context.Pending.TryDequeue(out var continuation)) continuation.Callback(continuation.State);
            thread.Join(10);
        }
        if (error != null) throw new InvalidOperationException("UI lifecycle fixture failed.", error);
        return completed;
    }

    public static void Run(string root, Action<bool, string> check)
    {
        string libraryRoot = Path.Combine(root, "ui-close");
        check(WithoutPumping(() =>
        {
            using var library = new Library(libraryRoot);
            using var inbox = new LinkInbox(libraryRoot, _ => { }, _ => { });
        }), "Closing on the UI thread stops the inbox without needing the blocked dispatcher");
        using (var reopened = new Library(libraryRoot))
            check(reopened.State.Profiles.Count > 0, "Closing releases the library lock for the next launch");

        string pipeRoot = Path.Combine(root, "ui-forward");
        var delivered = new ConcurrentQueue<string>();
        using (var inbox = new LinkInbox(pipeRoot, delivered.Enqueue, _ => { }))
        {
            bool acknowledged = false;
            check(WithoutPumping(() => acknowledged = LinkInbox.Forward(pipeRoot, "", 500).GetAwaiter().GetResult()) && acknowledged,
                "Second launch can complete its focus handoff on the UI thread");
            check(delivered.TryDequeue(out var message) && message.Length == 0, "Focus request arrives exactly once");
        }
        bool absent = true;
        check(WithoutPumping(() => absent = LinkInbox.Forward(pipeRoot, "", 100).GetAwaiter().GetResult()) && !absent,
            "Missing receiver times out on the UI thread instead of leaving a windowless process");
    }
}
