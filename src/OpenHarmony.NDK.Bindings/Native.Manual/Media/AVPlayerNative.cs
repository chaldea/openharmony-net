using System.Runtime.InteropServices;

namespace OpenHarmony.NDK.Bindings.Native.Manual.Media;

// SDK: multimedia/player_framework/avplayer.h, API 12+ callbacks; no managed policy here.
public enum AVPlayerState { Idle, Initialized, Prepared, Playing, Paused, Stopped, Completed, Released, Error }
public enum AVPlayerInfoType { SeekDone, SpeedDone, BitrateDone, EndOfStream, StateChange, PositionUpdate }
public enum AVPlaybackSpeed { X075, X100, X125, X175, X200, X050, X150, X300, X025, X0125 }

public static unsafe partial class AVPlayerNative
{
    private const string Library = "libavplayer.so";
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_Create")]
    public static partial nint Create();
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetURLSource", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetUrlSource(nint player, string url);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetFDSource")]
    public static partial int SetFdSource(nint player, int fd, long offset, long size);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetVideoSurface")]
    public static partial int SetVideoSurface(nint player, nint window);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_Prepare")]
    public static partial int Prepare(nint player);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_Play")]
    public static partial int Play(nint player);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_Pause")]
    public static partial int Pause(nint player);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_ReleaseSync")]
    public static partial int ReleaseSync(nint player);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_Seek")]
    public static partial int Seek(nint player, int milliseconds, int mode);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetVolume")]
    public static partial int SetVolume(nint player, float left, float right);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetLooping")]
    public static partial int SetLooping(nint player, [MarshalAs(UnmanagedType.I1)] bool loop);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetPlaybackSpeed")]
    public static partial int SetPlaybackSpeed(nint player, AVPlaybackSpeed speed);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_GetCurrentTime")]
    public static partial int GetCurrentTime(nint player, out int milliseconds);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_GetDuration")]
    public static partial int GetDuration(nint player, out int milliseconds);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_GetVideoWidth")]
    public static partial int GetVideoWidth(nint player, out int width);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_GetVideoHeight")]
    public static partial int GetVideoHeight(nint player, out int height);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_GetState")]
    public static partial int GetState(nint player, out AVPlayerState state);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetOnInfoCallback")]
    public static partial int SetOnInfoCallback(nint player,
        delegate* unmanaged[Cdecl]<nint, AVPlayerInfoType, nint, nint, void> callback, nint userData);
    [LibraryImport(Library, EntryPoint = "OH_AVPlayer_SetOnErrorCallback")]
    public static partial int SetOnErrorCallback(nint player,
        delegate* unmanaged[Cdecl]<nint, int, byte*, nint, void> callback, nint userData);
    [LibraryImport("libnative_media_core.so", EntryPoint = "OH_AVFormat_GetIntValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GetIntValue(nint format, nint key, out int value);
}
