using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenHarmony.Graphics;
using OpenHarmony.Interop;
using OpenHarmony.NDK.Bindings.Native.Manual.Media;
using OpenHarmony.Threading;

namespace OpenHarmony.Media;

public enum MediaPlayerState { Initialized, Preparing, Ready, Playing, Paused, Ended, Error, Disposed }

/// <summary>API 15+ AVPlayer facade. All methods and events belong to Dispatcher.
/// Source is immutable; video output is optional for audio, and must be attached before PrepareAsync.</summary>
public sealed unsafe class MediaPlayer : IDisposable
{
    private nint _player;
    private readonly nint _cookie;
    private SafeFileHandle? _file;
    private NativeWindow? _surface;
    private readonly TaskCompletionSource _prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _play, _muted, _loop;
    private float _volume = 1, _rate = 1;
    private int _position;
    private bool IsPrepared => State is MediaPlayerState.Ready or MediaPlayerState.Playing or MediaPlayerState.Paused or MediaPlayerState.Ended;
    public PlatformDispatcher Dispatcher { get; }
    public MediaPlayerState State { get; private set; } = MediaPlayerState.Initialized;
    public TimeSpan Duration { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public PlatformException? Error { get; private set; }
    public event Action? StateChanged;
    public event Action? SeekCompleted;
    public event Action? PlaybackEnded;
    public event Action<PlatformException>? Failed;

    public MediaPlayer(string source, PlatformDispatcher dispatcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Dispatcher = dispatcher;
        dispatcher.VerifyAccess();
        _cookie = CallbackRegistry<MediaPlayer>.Add(this);
        try
        {
            _player = AVPlayerNative.Create();
            if (_player == 0) throw new PlatformException("AVPlayer.Create", -1);
            // Resolve the exported metadata key before entering unmanaged callbacks.
            _ = StateKey.Value;
            Check(AVPlayerNative.SetOnInfoCallback(_player, &OnInfo, _cookie), "SetOnInfoCallback");
            Check(AVPlayerNative.SetOnErrorCallback(_player, &OnError, _cookie), "SetOnErrorCallback");
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                Check(AVPlayerNative.SetUrlSource(_player, source), "SetUrlSource");
            else
            {
                if (uri != null && !uri.IsFile) throw new NotSupportedException($"Unsupported media URI scheme: {uri.Scheme}");
                var path = uri?.IsFile == true ? uri.LocalPath : source;
                _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var length = RandomAccess.GetLength(_file);
                if (length <= 0) throw new ArgumentException("The media file is empty.", nameof(source));
                Check(AVPlayerNative.SetFdSource(_player, checked((int)_file.DangerousGetHandle()), 0, length), "SetFdSource");
            }
        }
        catch { Dispose(); throw; }
    }

    public void SetVideoSurface(NativeWindow surface)
    {
        Verify();
        if (State != MediaPlayerState.Initialized || _surface != null)
            throw new InvalidOperationException("Attach one video surface before preparing the player.");
        if (!ReferenceEquals(surface.Dispatcher, Dispatcher))
            throw new ArgumentException("Player and surface must share a dispatcher.", nameof(surface));
        var handle = surface.Retain();
        try { Check(AVPlayerNative.SetVideoSurface(_player, handle), "SetVideoSurface"); _surface = surface; }
        catch { surface.ReleaseLease(); throw; }
    }

    /// <summary>Cancellation cancels the wait; Dispose releases/cancels the native operation.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        Verify();
        cancellationToken.ThrowIfCancellationRequested();
        if (State == MediaPlayerState.Initialized)
        {
            State = MediaPlayerState.Preparing;
            var result = AVPlayerNative.Prepare(_player);
            if (result != 0) Fail(new PlatformException("AVPlayer.Prepare", result));
        }
        return cancellationToken.CanBeCanceled ? _prepared.Task.WaitAsync(cancellationToken) : _prepared.Task;
    }

    public TimeSpan Position
    {
        get { TryGetPosition(out var position); return position; }
    }
    /// <summary>Returns false when AVPlayer cannot sample its clock during a state transition
    /// (notably paused seek). In that case position is the last known/accepted seek position.
    /// Playback failures are reported separately through Failed.</summary>
    public bool TryGetPosition(out TimeSpan position)
    {
        Verify();
        var value = 0;
        var sampled = IsPrepared && AVPlayerNative.GetCurrentTime(_player, out value) == 0;
        if (sampled) _position = Math.Max(0, value);
        position = TimeSpan.FromMilliseconds(_position);
        return sampled;
    }
    public bool Muted { get => _muted; set { Verify(); _muted = value; ApplyVolume(); } }
    public bool Loop { get => _loop; set { Verify(); _loop = value; if (IsPrepared) Check(AVPlayerNative.SetLooping(_player, value), "SetLooping"); } }
    public float Volume
    {
        get => _volume;
        set { Verify(); if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _volume = Math.Clamp(value, 0, 1); ApplyVolume(); }
    }
    public float Rate
    {
        get => _rate;
        set { Verify(); _rate = PlaybackRate.GetSupportedRate(value);
            if (IsPrepared) Check(AVPlayerNative.SetPlaybackSpeed(_player, PlaybackRate.GetNativeSpeed(_rate)), "SetPlaybackSpeed"); }
    }
    public void Play() { Verify(); _play = true; ApplyPlayback(); }
    public void Pause() { Verify(); _play = false; ApplyPlayback(); }
    public void Seek(TimeSpan position)
    {
        Verify();
        if (!IsPrepared) throw new InvalidOperationException("Prepare the player before seeking.");
        var milliseconds = (int)Math.Clamp(position.TotalMilliseconds, 0, int.MaxValue);
        Check(AVPlayerNative.Seek(_player, milliseconds, 2), "Seek");
        _position = milliseconds;
    }
    private void ApplyVolume()
    {
        if (IsPrepared) Check(AVPlayerNative.SetVolume(_player, _muted ? 0 : _volume, _muted ? 0 : _volume), "SetVolume");
    }
    private void ApplyPlayback()
    {
        if (!IsPrepared) return;
        if (_play && State != MediaPlayerState.Playing) Check(AVPlayerNative.Play(_player), "Play");
        else if (!_play && State == MediaPlayerState.Playing) Check(AVPlayerNative.Pause(_player), "Pause");
    }
    private void Verify()
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(State == MediaPlayerState.Disposed, this);
        if (Error != null) throw new InvalidOperationException("The player has failed; create a new player.", Error);
    }
    private static void Check(int result, string operation) => PlatformException.Check(result, "AVPlayer." + operation);
    private static class StateKey
    {
        // The system library remains loaded for process lifetime, as do LibraryImport bindings.
        internal static readonly nint Value = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load("libavplayer.so"), "OH_PLAYER_STATE"));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInfo(nint player, AVPlayerInfoType type, nint body, nint cookie)
    {
        try
        {
            var owner = CallbackRegistry<MediaPlayer>.Get(cookie);
            if (owner == null) return;
            var state = -1;
            if (type == AVPlayerInfoType.StateChange && body != 0 &&
                AVPlayerNative.GetIntValue(body, StateKey.Value, out var value)) state = value;
            owner.Dispatcher.Post(() => { if (owner.State != MediaPlayerState.Disposed) owner.ProcessInfo(type, state); });
        }
        catch { /* Never unwind through AVPlayer. */ }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(nint player, int code, byte* message, nint cookie)
    {
        try
        {
            var owner = CallbackRegistry<MediaPlayer>.Get(cookie);
            if (owner == null) return;
            var error = new PlatformException("AVPlayer", code, Marshal.PtrToStringUTF8((nint)message));
            owner.Dispatcher.Post(() => { if (owner.State != MediaPlayerState.Disposed) owner.Fail(error); });
        }
        catch { /* Never unwind through AVPlayer. */ }
    }
    private void ProcessInfo(AVPlayerInfoType type, int nativeState)
    {
        if (State == MediaPlayerState.Error) return;
        if (type == AVPlayerInfoType.SeekDone) { SeekCompleted?.Invoke(); return; }
        // EOS and Completed both arrive; Completed is the single non-loop end notification.
        if (type == AVPlayerInfoType.EndOfStream) { if (_loop) PlaybackEnded?.Invoke(); return; }
        if (type != AVPlayerInfoType.StateChange) return;
        if (nativeState < 0)
        {
            Check(AVPlayerNative.GetState(_player, out var current), "GetState");
            nativeState = (int)current;
        }
        var next = (AVPlayerState)nativeState switch
        {
            AVPlayerState.Prepared => MediaPlayerState.Ready,
            AVPlayerState.Playing => MediaPlayerState.Playing,
            AVPlayerState.Paused => MediaPlayerState.Paused,
            AVPlayerState.Completed => MediaPlayerState.Ended,
            AVPlayerState.Error => MediaPlayerState.Error,
            _ => State
        };
        if (next == State) return;
        if (next == MediaPlayerState.Error) { Fail(new PlatformException("AVPlayer.State", nativeState)); return; }
        State = next;
        try
        {
            if (next == MediaPlayerState.Ready)
            {
                Check(AVPlayerNative.GetVideoWidth(_player, out var width), "GetVideoWidth");
                Check(AVPlayerNative.GetVideoHeight(_player, out var height), "GetVideoHeight");
                Check(AVPlayerNative.GetDuration(_player, out var duration), "GetDuration");
                VideoWidth = width; VideoHeight = height; Duration = TimeSpan.FromMilliseconds(Math.Max(0, duration));
                ApplyVolume();
                Check(AVPlayerNative.SetLooping(_player, _loop), "SetLooping");
                Check(AVPlayerNative.SetPlaybackSpeed(_player, PlaybackRate.GetNativeSpeed(_rate)), "SetPlaybackSpeed");
                ApplyPlayback();
                _prepared.TrySetResult();
            }
            else if (next is MediaPlayerState.Playing or MediaPlayerState.Paused) ApplyPlayback();
        }
        catch (PlatformException error) { Fail(error); return; }
        StateChanged?.Invoke();
        if (next == MediaPlayerState.Ended && State != MediaPlayerState.Disposed) PlaybackEnded?.Invoke();
    }
    private void Fail(PlatformException error)
    {
        Error = error; State = MediaPlayerState.Error;
        _prepared.TrySetException(error);
        Failed?.Invoke(error);
        StateChanged?.Invoke();
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess();
        if (State == MediaPlayerState.Disposed) return;
        State = MediaPlayerState.Disposed;
        CallbackRegistry<MediaPlayer>.Remove(_cookie);
        if (_player != 0)
        {
            AVPlayerNative.SetOnInfoCallback(_player, null, 0);
            AVPlayerNative.SetOnErrorCallback(_player, null, 0);
            AVPlayerNative.ReleaseSync(_player);
            _player = 0;
        }
        _surface?.ReleaseLease(); _surface = null;
        _file?.Dispose(); _file = null;
        _prepared.TrySetCanceled();
        StateChanged = null; SeekCompleted = null; PlaybackEnded = null; Failed = null;
    }
}
