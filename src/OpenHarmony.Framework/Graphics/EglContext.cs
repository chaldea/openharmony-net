using OpenHarmony.NDK.Bindings.Native.Manual.Graphics;
using OpenHarmony.Threading;

namespace OpenHarmony.Graphics;

/// <summary>A borrowed EGL context, valid until the owning surface is destroyed.</summary>
public sealed class EglContext
{
    private readonly nint _context, _display, _draw, _read;
    public PlatformDispatcher Dispatcher { get; }
    private EglContext(PlatformDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
        dispatcher.VerifyAccess();
        _context = EglNative.GetCurrentContext();
        _display = EglNative.GetCurrentDisplay();
        _draw = EglNative.GetCurrentSurface(0x3059);
        _read = EglNative.GetCurrentSurface(0x305A);
        if (_context == 0 || _display == 0) throw new InvalidOperationException("An EGL context must be current.");
    }
    public static EglContext CaptureCurrent(PlatformDispatcher dispatcher) => new(dispatcher);
    public void MakeCurrent()
    {
        Dispatcher.VerifyAccess();
        if (EglNative.MakeCurrent(_display, _draw, _read, _context) == 0)
            throw new InvalidOperationException("Cannot make the owning EGL context current.");
    }
    public nint GetProcAddress(string name)
    {
        Dispatcher.VerifyAccess();
        return EglNative.GetProcAddress(name) is var address && address != 0 ? address
            : throw new NotSupportedException($"OpenGL function {name} is unavailable.");
    }
}
