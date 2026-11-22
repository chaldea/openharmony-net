using System.Collections.Concurrent;

namespace OpenHarmony.Threading;

/// <summary>Serial owner-thread queue. Hosts drain it before rendering, including idle frames.</summary>
public sealed class PlatformDispatcher
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();
    public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread;
    public void VerifyAccess()
    {
        if (!CheckAccess()) throw new InvalidOperationException("This platform object belongs to another thread. Use its dispatcher.");
    }
    public void Post(Action action) => _queue.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));
    public void Drain()
    {
        VerifyAccess();
        // Bound one frame's work; newly queued work may run on the next frame.
        var count = _queue.Count;
        for (var i = 0; i < count && _queue.TryDequeue(out var action); i++) action();
    }
}
