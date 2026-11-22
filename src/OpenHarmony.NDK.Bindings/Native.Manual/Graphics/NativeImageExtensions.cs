using System.Runtime.InteropServices;
using OpenHarmony.NDK.Bindings.Native.Generated.Foundation;

namespace OpenHarmony.NDK.Bindings.Native.Manual.Graphics;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeImageFrameListener
{
    public nint Context;
    public delegate* unmanaged[Cdecl]<nint, void> OnFrameAvailable;
}

public static unsafe partial class NativeImageExtensions
{
    [LibraryImport("libnative_image.so", EntryPoint = "OH_NativeImage_SetOnFrameAvailableListener")]
    public static partial int SetOnFrameAvailableListener(OH_NativeImage* image, NativeImageFrameListener listener);
    [LibraryImport("libnative_image.so", EntryPoint = "OH_NativeImage_GetTransformMatrixV2")]
    public static partial int GetTransformMatrixV2(OH_NativeImage* image, float* matrix16);
    [LibraryImport("libnative_window.so", EntryPoint = "OH_NativeWindow_DestroyNativeWindow")]
    public static partial void DestroyNativeWindow(nint window);
}

public static partial class EglNative
{
    [LibraryImport("libEGL.so", EntryPoint = "eglGetCurrentContext")]
    public static partial nint GetCurrentContext();
    [LibraryImport("libEGL.so", EntryPoint = "eglGetCurrentDisplay")]
    public static partial nint GetCurrentDisplay();
    [LibraryImport("libEGL.so", EntryPoint = "eglGetCurrentSurface")]
    public static partial nint GetCurrentSurface(int readDraw);
    [LibraryImport("libEGL.so", EntryPoint = "eglMakeCurrent")]
    public static partial int MakeCurrent(nint display, nint draw, nint read, nint context);
    [LibraryImport("libEGL.so", EntryPoint = "eglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(string name);
}
