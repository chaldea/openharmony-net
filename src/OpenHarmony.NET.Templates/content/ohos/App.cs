using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenHarmony.Hosting;
using OpenHarmony.Views;

public static class App
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_initialize", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Initialize() => SurfaceApplication.Initialize(() => new AppView());
}

sealed unsafe class AppView : SurfaceView
{
    private const uint ColorBufferBit = 0x4000; // GL_COLOR_BUFFER_BIT
    private delegate* unmanaged[Cdecl]<uint, void> _clear;

    public override void OnCreated()
    {
        var clearColor = (delegate* unmanaged[Cdecl]<float, float, float, float, void>)
            GraphicsContext.GetProcAddress("glClearColor");
        _clear = (delegate* unmanaged[Cdecl]<uint, void>)GraphicsContext.GetProcAddress("glClear");
        clearColor(0.12f, 0.27f, 0.44f, 1f);
    }

    public override bool OnFrame(SurfaceFrame frame)
    {
        _clear(ColorBufferBit);
        return true;
    }
}
