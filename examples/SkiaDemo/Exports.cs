using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using OpenHarmony.Hosting;
using OpenHarmony.Views;
using SkiaSharp;

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_initialize", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Initialize() => SurfaceApplication.Initialize(() => new SkiaView());
}

sealed class SkiaView : SurfaceView
{
    private GRContext context = null!;
    private (int Width, int Height) drawnSize;

    public override void OnCreated()
    {
        using var gl = GRGlInterface.CreateGles(ResolveGl)
            ?? throw new InvalidOperationException("Cannot load the GLES interface.");
        context = GRContext.CreateGl(gl) ?? throw new InvalidOperationException("Cannot initialize Skia GLES.");
    }

    private nint ResolveGl(string name)
    {
        // Skia also probes optional extension functions.
        try { return GraphicsContext.GetProcAddress(name); }
        catch (NotSupportedException) { return 0; }
    }

    public override bool OnFrame(SurfaceFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || drawnSize == (frame.Width, frame.Height))
            return false;

        // The EGL window surface exposes framebuffer 0 with RGBA8 and an 8-bit stencil.
        using var target = new GRBackendRenderTarget(frame.Width, frame.Height, 0, 8,
            new GRGlFramebufferInfo(0, 0x8058)); // GL_RGBA8
        using var surface = SKSurface.Create(context, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Cannot wrap the window framebuffer.");
        var canvas = surface.Canvas;
        float scale = Math.Min(frame.Width / 640f, frame.Height / 800f);
        canvas.Translate((frame.Width - 640 * scale) / 2, (frame.Height - 800 * scale) / 2);
        canvas.Scale(scale);
        int glyphs = Draw(canvas);
        context.Flush();
        drawnSize = (frame.Width, frame.Height);
        SurfaceApplication.Log($"SKIA_DEMO rendered=1 pixels={frame.Width}x{frame.Height} glyphs={glyphs}");
        return true;
    }

    public override void OnDestroyed() => context?.Dispose();

    private static int Draw(SKCanvas canvas)
    {
        canvas.Clear(SKColor.Parse("#101828"));
        using var paint = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateLinearGradient(new SKPoint(40, 170), new SKPoint(600, 540),
            [SKColor.Parse("#2DD4BF"), SKColor.Parse("#818CF8")], SKShaderTileMode.Clamp);
        paint.Shader = shader;
        canvas.DrawRoundRect(new SKRect(40, 170, 600, 540), 32, 32, paint);
        paint.Shader = null;
        paint.Color = new SKColor(255, 255, 255, 190);
        canvas.DrawCircle(210, 320, 90, paint);
        paint.Color = SKColor.Parse("#101828");
        using var pathBuilder = new SKPathBuilder();
        pathBuilder.MoveTo(330, 430);
        pathBuilder.CubicTo(350, 190, 500, 190, 540, 430);
        pathBuilder.Close();
        using var path = pathBuilder.Detach();
        canvas.DrawPath(path, paint);

        // Use the same system font in HarfBuzz and the Skia glyph renderer.
        const string fontPath = "/system/fonts/HarmonyOS_Sans.ttf";
        using var typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidOperationException("HarmonyOS Sans is unavailable.");
        using var headingFont = new SKFont(typeface, 45);
        using var captionFont = new SKFont(typeface, 23);
        paint.Color = SKColors.White;
        canvas.DrawText("Skia on HarmonyOS", 40, 88, SKTextAlign.Left, headingFont, paint);
        paint.Color = SKColor.Parse("#98A2B3");
        canvas.DrawText("C# / .NET 11 NativeAOT", 40, 130, SKTextAlign.Left, captionFont, paint);

        using var blob = Blob.FromFile(fontPath);
        using var face = new Face(blob, 0);
        using var font = new HarfBuzzSharp.Font(face);
        font.SetFunctionsOpenType();
        font.SetScale(42 * 64, 42 * 64);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf8("Hello, HarmonyOS!");
        buffer.GuessSegmentProperties();
        font.Shape(buffer);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        if (infos.Length == 0 || infos.Any(info => info.Codepoint == 0)) throw new InvalidOperationException("Missing shaped glyphs.");
        using var skFont = new SKFont(typeface, 42);
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(skFont, infos.Length);
        var glyphs = run.Glyphs;
        var points = run.Positions;
        float x = 40, y = 630;
        for (var i = 0; i < infos.Length; i++)
        {
            glyphs[i] = checked((ushort)infos[i].Codepoint);
            points[i] = new SKPoint(x + positions[i].XOffset / 64f, y - positions[i].YOffset / 64f);
            x += positions[i].XAdvance / 64f;
            y -= positions[i].YAdvance / 64f;
        }
        using var text = builder.Build();
        paint.Color = SKColors.White;
        canvas.DrawText(text, 0, 0, paint);
        paint.Color = SKColor.Parse("#98A2B3");
        canvas.DrawText($"HarfBuzz shaped {infos.Length} glyphs", 40, 683, SKTextAlign.Left, captionFont, paint);
        return infos.Length;
    }
}
