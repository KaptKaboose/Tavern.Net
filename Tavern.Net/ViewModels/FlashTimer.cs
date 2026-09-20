using System.Windows.Threading;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Drives a "just changed" highlight: <see cref="Trigger"/> turns a flag on and, after
/// <see cref="Duration"/>, turns it back off. Triggering again while it is still lit restarts the
/// countdown, so a burst of changes keeps it lit and it fades one duration after the last one.
/// The flag itself is whatever the caller passes in (usually an <c>[ObservableProperty]</c> bool a
/// XAML DataTrigger watches), so this class only owns the timing. Must be used on the UI thread.
/// </summary>
public sealed class FlashTimer
{
    /// <summary>How long the highlight stays lit after the most recent <see cref="Trigger"/>.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(1);

    private readonly Action<bool> _setLit;
    private readonly DispatcherTimer _timer;

    /// <param name="setLit">Called with true when the flash starts and false when it ends.</param>
    /// <param name="duration">Defaults to <see cref="DefaultDuration"/>.</param>
    public FlashTimer(Action<bool> setLit, TimeSpan? duration = null)
    {
        _setLit = setLit;
        _timer = new DispatcherTimer { Interval = duration ?? DefaultDuration };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _setLit(false);
        };
    }

    public TimeSpan Duration => _timer.Interval;

    public void Trigger()
    {
        _setLit(true);
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Ends the flash immediately, if it is lit.</summary>
    public void Stop()
    {
        _timer.Stop();
        _setLit(false);
    }
}
