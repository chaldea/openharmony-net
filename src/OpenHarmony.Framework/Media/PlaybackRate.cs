using OpenHarmony.NDK.Bindings.Native.Manual.Media;

namespace OpenHarmony.Media;

public static class PlaybackRate
{
    public static float GetSupportedRate(float requested) => Select(requested).Rate;
    internal static AVPlaybackSpeed GetNativeSpeed(float requested) => Select(requested).Speed;
    private static (float Rate, AVPlaybackSpeed Speed) Select(float requested)
    {
        if (!float.IsFinite(requested) || requested <= 0) throw new ArgumentOutOfRangeException(nameof(requested));
        ReadOnlySpan<(float Rate, AVPlaybackSpeed Speed)> rates =
        [(.125f, AVPlaybackSpeed.X0125), (.25f, AVPlaybackSpeed.X025), (.5f, AVPlaybackSpeed.X050),
         (.75f, AVPlaybackSpeed.X075), (1, AVPlaybackSpeed.X100), (1.25f, AVPlaybackSpeed.X125),
         (1.5f, AVPlaybackSpeed.X150), (1.75f, AVPlaybackSpeed.X175), (2, AVPlaybackSpeed.X200), (3, AVPlaybackSpeed.X300)];
        var nearest = rates[0];
        foreach (var rate in rates)
            if (Math.Abs(rate.Rate - requested) < Math.Abs(nearest.Rate - requested)) nearest = rate;
        return nearest;
    }
}
