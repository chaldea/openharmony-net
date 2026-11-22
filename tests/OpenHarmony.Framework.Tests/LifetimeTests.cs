using OpenHarmony.Graphics;
using OpenHarmony.Interop;
using OpenHarmony.Media;
using OpenHarmony.Threading;

namespace OpenHarmony.Framework.Tests;

public class LifetimeTests
{
    [Fact]
    public void NativeCallbacksAreDeferredToOwnerAndRemainOrdered()
    {
        var dispatcher = new PlatformDispatcher();
        var owner = Environment.CurrentManagedThreadId;
        List<int> result = [];
        var worker = new Thread(() =>
        {
            Assert.False(dispatcher.CheckAccess());
            Assert.Throws<InvalidOperationException>(dispatcher.Drain);
            dispatcher.Post(() => { Assert.Equal(owner, Environment.CurrentManagedThreadId); result.Add(1); });
            dispatcher.Post(() => result.Add(2));
        });
        worker.Start(); worker.Join();
        Assert.Empty(result);
        dispatcher.Drain();
        Assert.Equal([1, 2], result);
    }
    [Fact]
    public void RepostedWorkCannotStarveTheFrame()
    {
        var dispatcher = new PlatformDispatcher();
        var calls = 0;
        void Work() { calls++; dispatcher.Post(Work); }
        dispatcher.Post(Work);
        dispatcher.Drain();
        Assert.Equal(1, calls);
        dispatcher.Drain();
        Assert.Equal(2, calls);
    }
    [Fact]
    public void LateNativeCallbackCannotTargetAReleasedOrReusedObject()
    {
        var original = new object();
        var cookie = CallbackRegistry<object>.Add(original);
        Assert.Same(original, CallbackRegistry<object>.Get(cookie));
        CallbackRegistry<object>.Remove(cookie);
        var replacement = CallbackRegistry<object>.Add(new object());
        try { Assert.Null(CallbackRegistry<object>.Get(cookie)); Assert.NotEqual(cookie, replacement); }
        finally { CallbackRegistry<object>.Remove(replacement); }
    }
    [Fact]
    public void SurfaceCannotBeDestroyedWhileAProducerRetainsIt()
    {
        using var window = NativeWindow.Borrow(42, new PlatformDispatcher());
        Assert.Equal(42, window.Retain());
        Assert.Throws<InvalidOperationException>(window.Dispose);
        window.ReleaseLease();
        window.Dispose();
        window.Dispose();
        Assert.Throws<ObjectDisposedException>(() => window.Retain());
    }
    [Fact]
    public void BorrowedSurfaceStillRequiresOwnerThreadForRelease()
    {
        using var window = NativeWindow.Borrow(42, new PlatformDispatcher());
        var worker = new Thread(() => Assert.Throws<InvalidOperationException>(window.Dispose));
        worker.Start(); worker.Join();
    }
    [Theory]
    [InlineData(.125f, 9)] [InlineData(.25f, 8)] [InlineData(.5f, 5)]
    [InlineData(.75f, 0)] [InlineData(1f, 1)] [InlineData(1.25f, 2)]
    [InlineData(1.5f, 6)] [InlineData(1.75f, 3)] [InlineData(2f, 4)] [InlineData(3f, 7)]
    public void PlaybackSpeedAbiMatchesApi15(float rate, int value)
        => Assert.Equal(value, (int)PlaybackRate.GetNativeSpeed(rate));
    [Fact]
    public void PlaybackSpeedValidatesAndChoosesNearestSupportedValue()
    {
        Assert.Equal(1.25f, PlaybackRate.GetSupportedRate(1.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackRate.GetSupportedRate(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackRate.GetSupportedRate(0));
    }
}
