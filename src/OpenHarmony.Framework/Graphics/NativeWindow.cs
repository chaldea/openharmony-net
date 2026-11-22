using OpenHarmony.NDK.Bindings.Native.Manual.Graphics;
using OpenHarmony.Threading;

namespace OpenHarmony.Graphics;

/// <summary>A native producer surface. A player retains a lease until it is released.</summary>
public sealed class NativeWindow : IDisposable
{
    private nint _handle;
    private readonly bool _ownsHandle;
    private int _leases;
    public PlatformDispatcher Dispatcher { get; }
    internal NativeWindow(nint handle, PlatformDispatcher dispatcher, bool ownsHandle)
    {
        if (handle == 0) throw new ArgumentException("A native window is required.", nameof(handle));
        _handle = handle;
        Dispatcher = dispatcher;
        _ownsHandle = ownsHandle;
    }
    /// <summary>The external owner must keep this window alive until all consumers are disposed.</summary>
    public static NativeWindow Borrow(nint handle, PlatformDispatcher dispatcher) => new(handle, dispatcher, false);
    internal nint Retain()
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        _leases++;
        return _handle;
    }
    internal void ReleaseLease() { Dispatcher.VerifyAccess(); _leases--; }
    internal void VerifyCanDispose()
    {
        Dispatcher.VerifyAccess();
        if (_leases != 0) throw new InvalidOperationException("Release the player or other producers before disposing the surface.");
    }
    public void Dispose()
    {
        VerifyCanDispose();
        if (_handle == 0) return;
        if (_ownsHandle) NativeImageExtensions.DestroyNativeWindow(_handle);
        _handle = 0;
    }
}
