using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenHarmony.Graphics;
using OpenHarmony.Views;

namespace OpenHarmony.Hosting;

/// <summary>NativeAOT entry point wiring for the bundled XComponent host. One application per process.</summary>
public static unsafe class SurfaceApplication
{
    private static Func<SurfaceView>? _factory;
    private static SurfaceView? _host;
    private static delegate* unmanaged[Cdecl]<byte*, void> _log;
    private static bool _failed;
    public static SurfaceView? Current => _host;
    private static readonly NativeCallbacks* Callbacks = CreateCallbacks();

    /// <summary>Called by the application's exported openharmony_initialize function.</summary>
    public static nint Initialize(Func<SurfaceView> contextFactory)
    {
        _factory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        return (nint)Callbacks;
    }

    private static NativeCallbacks* CreateCallbacks()
    {
        // Function table is process lifetime; the C++ host retains it across surface recreation.
        var table = (NativeCallbacks*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(SurfaceApplication), sizeof(NativeCallbacks));
        *table = new NativeCallbacks
        {
            Version = 1, Create = &Create, Frame = &Frame, Destroy = &Destroy,
            Touch = &Touch, Back = &Back, Active = &Active, Text = &Text, Key = &Key, Editor = &Editor, Navigate = &Navigate
        };
        return table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Create(delegate* unmanaged[Cdecl]<byte*, nint> getProc, delegate* unmanaged[Cdecl]<byte*, void> log)
    {
        _log = log;
        _failed = false;
        try
        {
            _host?.OnDestroyed();
            _host = (_factory ?? throw new InvalidOperationException("Call Initialize first."))();
            _host.GraphicsContext = EglContext.CaptureCurrent(_host.Dispatcher);
            _host.OnCreated();
            Log($"OH_FRAMEWORK created runtime={Environment.Version.Major} backend=OpenGL");
            return 1;
        }
        catch (Exception error) { Fail(error); return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Frame(int width, int height, float density, double seconds)
    {
        if (_failed) return -1;
        try
        {
            if (_host == null) return 0;
            _host.Dispatcher.Drain();
            return _host.OnFrame(new SurfaceFrame(width, height, density, seconds)) ? 1 : 0;
        }
        catch (Exception error) { Fail(error); return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Destroy()
    {
        try { _host?.OnDestroyed(); }
        catch (Exception error) { Fail(error); }
        finally { _host = null; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Touch(int action, int id, float x, float y)
    {
        try
        {
            if (_failed || _host == null) return;
            _host.OnTouch(action, id, x, y);
        }
        catch (Exception error) { Fail(error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Back()
    {
        try { return !_failed && _host?.OnBack() == true ? 1 : 0; }
        catch (Exception error) { Fail(error); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Active(int active)
    {
        try { if (!_failed) _host?.OnActiveChanged(active != 0); }
        catch (Exception error) { Fail(error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Text(byte* text, int length)
    {
        try { if (!_failed && text != null && length > 0) _host?.OnText(Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, length))); }
        catch (Exception error) { Fail(error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Key(int key)
    {
        try { if (!_failed && Enum.IsDefined((EditingKey)key)) _host?.OnKey((EditingKey)key); }
        catch (Exception error) { Fail(error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Editor(byte* buffer, int capacity)
    {
        try
        {
            if (_host == null || _failed) return 0;
            var state = _host.SerializeEditor();
            var count = Encoding.UTF8.GetByteCount(state);
            if (buffer != null && capacity >= count) Encoding.UTF8.GetBytes(state, new Span<byte>(buffer, capacity));
            return count;
        }
        catch (Exception error) { Fail(error); return 0; }
    }

    private static void Fail(Exception error) { _failed = true; Log("OH_FRAMEWORK_ERROR " + error); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Navigate(byte* path, int length)
    {
        try
        {
            if (_failed || path == null || length <= 0 || length > 4096) return;
            var route = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(path, length));
            if (route.StartsWith('/') && !route.StartsWith("//", StringComparison.Ordinal)) _host?.OnNavigate(route);
        }
        catch (Exception error) { Fail(error); }
    }
    public static void Log(string message)
    {
        if (_log == null) return;
        var bytes = Encoding.UTF8.GetBytes(message + '\0');
        fixed (byte* p = bytes) _log(p);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCallbacks
    {
        public int Version;
        public delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<byte*, nint>, delegate* unmanaged[Cdecl]<byte*, void>, int> Create;
        public delegate* unmanaged[Cdecl]<int, int, float, double, int> Frame;
        public delegate* unmanaged[Cdecl]<void> Destroy;
        public delegate* unmanaged[Cdecl]<int, int, float, float, void> Touch;
        public delegate* unmanaged[Cdecl]<int> Back;
        public delegate* unmanaged[Cdecl]<int, void> Active;
        public delegate* unmanaged[Cdecl]<byte*, int, void> Text;
        public delegate* unmanaged[Cdecl]<int, void> Key;
        public delegate* unmanaged[Cdecl]<byte*, int, int> Editor;
        public delegate* unmanaged[Cdecl]<byte*, int, void> Navigate;
    }
}
