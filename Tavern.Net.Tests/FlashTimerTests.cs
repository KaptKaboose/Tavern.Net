using Tavern.Net.ViewModels;

namespace Tavern.Net.Tests;

public class FlashTimerTests
{
    // Real timers on the WPF test thread, so the durations are short but the margins are generous.
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);

    private sealed class Flag
    {
        public volatile bool Lit;
        public FlashTimer Timer = null!;
    }

    private static Flag Create() => WpfTestHost.Invoke(() =>
    {
        var flag = new Flag();
        flag.Timer = new FlashTimer(lit => flag.Lit = lit, Duration);
        return flag;
    });

    [Fact]
    public void Trigger_LightsTheFlagAtOnce_ThenTurnsItOffAfterTheDuration()
    {
        var flag = Create();

        WpfTestHost.Invoke(() => flag.Timer.Trigger());
        Assert.True(flag.Lit);

        Thread.Sleep(Duration + TimeSpan.FromMilliseconds(500));
        Assert.False(flag.Lit);
    }

    [Fact]
    public void TriggeringAgainWhileLit_RestartsTheCountdown()
    {
        var flag = Create();

        WpfTestHost.Invoke(() => flag.Timer.Trigger());
        Thread.Sleep(200);
        WpfTestHost.Invoke(() => flag.Timer.Trigger());
        Thread.Sleep(200);
        Assert.True(flag.Lit);      // 400ms after the first trigger, but only 200ms after the second

        Thread.Sleep(Duration + TimeSpan.FromMilliseconds(400));
        Assert.False(flag.Lit);
    }

    [Fact]
    public void Stop_TurnsItOffImmediately_AndItStaysOff()
    {
        var flag = Create();

        WpfTestHost.Invoke(() => flag.Timer.Trigger());
        WpfTestHost.Invoke(() => flag.Timer.Stop());
        Assert.False(flag.Lit);

        Thread.Sleep(Duration + TimeSpan.FromMilliseconds(300));
        Assert.False(flag.Lit);
    }
}
