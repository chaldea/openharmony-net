using System.Runtime.InteropServices;
using SkiaSharp;
using HarfBuzzSharp;

public static unsafe class Exports
{
    public const int Width = 640;
    public const int Height = 800;

    // ArkTS owns the ArrayBuffer. No managed object or exception crosses the C ABI.
    [UnmanagedCallersOnly(EntryPoint = "openharmony_skia_render")]
    public static int Render(byte* pixels, int length)
    {
        if (pixels == null || length != Width * Height * 4) return -1;
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(Width, Height,
                SKColorType.Rgba8888, SKAlphaType.Premul), (nint)pixels, Width * 4);
            if (surface is null) return -2;
            var canvas = surface.Canvas;
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
            using var path = new SKPath();
            path.MoveTo(330, 430);
            path.CubicTo(350, 190, 500, 190, 540, 430);
            path.Close();
            canvas.DrawPath(path, paint);

            // Use the same system font in HarfBuzz and the Skia glyph renderer.
            const string fontPath = "/system/fonts/HarmonyOS_Sans.ttf";
            using var typeface = SKTypeface.FromFile(fontPath)
                ?? throw new InvalidOperationException("HarmonyOS Sans is unavailable.");
            paint.Typeface = typeface;
            paint.TextSize = 45;
            paint.Color = SKColors.White;
            canvas.DrawText("Skia on HarmonyOS", 40, 88, paint);
            paint.TextSize = 23;
            paint.Color = SKColor.Parse("#98A2B3");
            canvas.DrawText("C# / .NET 10 NativeAOT", 40, 130, paint);

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
            if (infos.Length == 0 || infos.Any(info => info.Codepoint == 0)) return -3;
            using var skFont = new SKFont(typeface, 42);
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(skFont, infos.Length);
            var glyphs = run.GetGlyphSpan();
            var points = run.GetPositionSpan();
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
            paint.TextSize = 23;
            paint.Color = SKColor.Parse("#98A2B3");
            canvas.DrawText($"HarfBuzz shaped {infos.Length} glyphs", 40, 683, paint);
            canvas.Flush();
            return infos.Length;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"SKIA_RENDER_FAILED: {error}");
            return -4;
        }
    }
}
