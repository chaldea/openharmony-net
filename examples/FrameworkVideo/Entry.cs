using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenHarmony.Graphics;
using OpenHarmony.Hosting;
using OpenHarmony.Media;
using OpenHarmony.Views;

public static class Entry
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_initialize", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Initialize() => SurfaceApplication.Initialize(() => new VideoProbe());
}

// No UI framework dependency. Launch, then send the /video route to start the bundled fixture:
// aa start -b org.openharmony.frameworkvideo -a EntryAbility --ps openharmonyRoute /video
// This diagnostic consumes GPU frames and logs metadata; it does not draw a player UI.
sealed class VideoProbe : SurfaceView
{
    private MediaPlayer? _player;
    private NativeImage? _output;
    private int _frames;
    public override void OnNavigate(string path)
    {
        if (path == "/pause") { _player?.Pause(); return; }
        if (path == "/seek") { _player?.Seek(TimeSpan.FromSeconds(2)); return; }
        if (path == "/resume") { _player?.Play(); return; }
        if (path == "/release") { Release(); SurfaceApplication.Log("FRAMEWORK_VIDEO released"); return; }
        if (path == "/invalid")
        {
            Release();
            var invalid = Path.Combine(Path.GetTempPath(), "invalid.mp4");
            File.WriteAllText(invalid, "not a media container");
            try
            {
                _player = new MediaPlayer(invalid, Dispatcher);
                _player.Failed += error => SurfaceApplication.Log("FRAMEWORK_EXPECTED_MEDIA_ERROR " + error.Message);
                _ = Observe(_player.PrepareAsync(), expectedFailure: true);
            }
            catch (Exception error) { SurfaceApplication.Log("FRAMEWORK_EXPECTED_MEDIA_ERROR " + error.Message); Release(); }
            return;
        }
        if (path != "/video" || _player != null) return;
        var source = Path.Combine(Path.GetTempPath(), "framework-video.mp4");
        using (var input = typeof(Entry).Assembly.GetManifestResourceStream("FrameworkVideo.sample.mp4")!)
        using (var file = File.Create(source)) input.CopyTo(file);
        _player = new MediaPlayer(source, Dispatcher) { Loop = true, Muted = true };
        _output = new NativeImage(GraphicsContext);
        _player.SetVideoSurface(_output.Surface);
        _player.StateChanged += () => SurfaceApplication.Log($"FRAMEWORK_VIDEO state={_player.State} size={_player.VideoWidth}x{_player.VideoHeight}");
        _player.Failed += error => SurfaceApplication.Log("FRAMEWORK_VIDEO_ERROR " + error);
        _player.SeekCompleted += () => SurfaceApplication.Log($"FRAMEWORK_VIDEO seek={_player.Position.TotalMilliseconds}");
        _player.Play();
        _ = Observe(_player.PrepareAsync());
    }
    private async Task Observe(Task prepared, bool expectedFailure = false)
    {
        try { await prepared; }
        catch (OperationCanceledException) { }
        catch (Exception error) { SurfaceApplication.Log((expectedFailure ? "FRAMEWORK_EXPECTED_MEDIA_ERROR " : "FRAMEWORK_VIDEO_ERROR ") + error.Message); }
    }
    public override bool OnFrame(SurfaceFrame frame)
    {
        if (_output?.TryAcquireFrame(out var video) == true && ++_frames % 30 == 1)
            SurfaceApplication.Log($"FRAMEWORK_VIDEO frame={_frames} texture={video.TextureId} timestamp={video.Timestamp} transform={video.TextureTransform}");
        return false;
    }
    private void Release() { _player?.Dispose(); _output?.Dispose(); _player = null; _output = null; _frames = 0; }
    public override void OnDestroyed() => Release();
}
