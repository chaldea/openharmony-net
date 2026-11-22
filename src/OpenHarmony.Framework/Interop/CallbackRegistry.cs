using System.Collections.Concurrent;

namespace OpenHarmony.Interop;

// Monotonic cookies are never dereferenced. A late callback after removal is harmless,
// unlike a freed GCHandle. Native callbacks only copy data and post managed work.
internal static class CallbackRegistry<T> where T : class
{
    private static long _next;
    private static readonly ConcurrentDictionary<nint, T> Entries = new();
    internal static nint Add(T value)
    {
        var id = checked((nint)Interlocked.Increment(ref _next));
        Entries[id] = value;
        return id;
    }
    internal static T? Get(nint id) => Entries.GetValueOrDefault(id);
    internal static void Remove(nint id) => Entries.TryRemove(id, out _);
}
