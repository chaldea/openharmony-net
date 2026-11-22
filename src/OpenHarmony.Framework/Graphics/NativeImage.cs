using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenHarmony.Interop;
using OpenHarmony.NDK.Bindings.Native.Generated.Foundation;
using OpenHarmony.NDK.Bindings.Native.Manual.Graphics;

namespace OpenHarmony.Graphics;

/// <summary>Latest external OES texture. Transform uses System.Numerics row-vector convention;
/// it is the complete native column-major texture matrix, including crop and rotation.</summary>
// NativeImage forwards the producer timestamp without specifying a universal unit.
// In particular, AVPlayer on some devices supplies presentation timestamps in microseconds.
public readonly record struct NativeImageFrame(uint TextureId, long Timestamp, Matrix4x4 TextureTransform);

/// <summary>GPU consumer independent of MediaPlayer; also usable by camera/video producers.
/// Create, update and dispose on the owning EGL thread, before the context is destroyed.</summary>
public sealed unsafe class NativeImage : IDisposable
{
    public const uint TextureTarget = 0x8D65;
    private readonly EglContext _context;
    private OH_NativeImage* _image;
    private uint _texture;
    private readonly nint _cookie;
    private int _pending, _notification;
    private volatile bool _disposed;
    public NativeWindow Surface { get; private set; } = null!;
    public event Action? FrameAvailable;

    public NativeImage(EglContext context)
    {
        _context = context;
        context.MakeCurrent();
        _cookie = CallbackRegistry<NativeImage>.Add(this);
        try
        {
            var gen = (delegate* unmanaged[Cdecl]<int, uint*, void>)context.GetProcAddress("glGenTextures");
            var bind = (delegate* unmanaged[Cdecl]<uint, uint, void>)context.GetProcAddress("glBindTexture");
            var parameter = (delegate* unmanaged[Cdecl]<uint, uint, int, void>)context.GetProcAddress("glTexParameteri");
            var get = (delegate* unmanaged[Cdecl]<uint, int*, void>)context.GetProcAddress("glGetIntegerv");
            int previous = 0;
            get(0x8D67, &previous);
            uint texture = 0;
            gen(1, &texture);
            _texture = texture;
            if (texture == 0) throw new InvalidOperationException("Could not allocate an external texture.");
            bind(TextureTarget, texture);
            parameter(TextureTarget, 0x2801, 0x2601); // MIN_FILTER, LINEAR
            parameter(TextureTarget, 0x2800, 0x2601);
            parameter(TextureTarget, 0x2802, 0x812F); // WRAP_S/T, CLAMP_TO_EDGE
            parameter(TextureTarget, 0x2803, 0x812F);
            bind(TextureTarget, (uint)previous);
            _image = NativeImageNative.Create(texture, TextureTarget);
            if (_image == null) throw new InvalidOperationException("OH_NativeImage_Create failed.");
            Surface = new NativeWindow((nint)NativeImageNative.AcquireNativeWindow(_image), context.Dispatcher, true);
            PlatformException.Check(NativeImageExtensions.SetOnFrameAvailableListener(_image,
                new NativeImageFrameListener { Context = _cookie, OnFrameAvailable = &OnFrame }), "NativeImage.SetListener");
        }
        catch { Dispose(); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(nint cookie)
    {
        try
        {
            var image = CallbackRegistry<NativeImage>.Get(cookie);
            if (image == null || image._disposed) return;
            Interlocked.Exchange(ref image._pending, 1);
            if (Interlocked.Exchange(ref image._notification, 1) == 0)
                image._context.Dispatcher.Post(() =>
                {
                    Interlocked.Exchange(ref image._notification, 0);
                    if (!image._disposed) image.FrameAvailable?.Invoke();
                });
        }
        catch { /* No managed exception may unwind through a native callback. */ }
    }

    public bool TryAcquireFrame(out NativeImageFrame frame)
    {
        _context.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = default;
        if (Interlocked.Exchange(ref _pending, 0) == 0) return false;
        _context.MakeCurrent();
        PlatformException.Check(NativeImageNative.UpdateSurfaceImage(_image), "NativeImage.UpdateSurfaceImage");
        float* matrix = stackalloc float[16];
        PlatformException.Check(NativeImageExtensions.GetTransformMatrixV2(_image, matrix), "NativeImage.GetTransformMatrixV2");
        frame = new(_texture, NativeImageNative.GetTimestamp(_image), new Matrix4x4(
            matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7],
            matrix[8], matrix[9], matrix[10], matrix[11], matrix[12], matrix[13], matrix[14], matrix[15]));
        return true;
    }

    public void Dispose()
    {
        _context.Dispatcher.VerifyAccess();
        if (_disposed) return;
        Surface?.VerifyCanDispose();
        _context.MakeCurrent();
        _disposed = true;
        CallbackRegistry<NativeImage>.Remove(_cookie);
        FrameAvailable = null;
        if (_image != null) NativeImageNative.UnsetOnFrameAvailableListener(_image);
        Surface?.Dispose();
        var image = _image;
        _image = null;
        if (image != null) NativeImageNative.Destroy(&image);
        if (_texture != 0)
        {
            var delete = (delegate* unmanaged[Cdecl]<int, uint*, void>)_context.GetProcAddress("glDeleteTextures");
            var texture = _texture;
            delete(1, &texture);
            _texture = 0;
        }
    }
}
