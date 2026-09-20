using System.Windows;
using System.Windows.Threading;

namespace Tavern.Net.Tests;

/// <summary>
/// One shared STA thread running a WPF dispatcher, for tests that need WPF objects: they belong to the
/// thread that made them, an Application can only exist once per process, and a DispatcherTimer only
/// ticks while its dispatcher is running. The Application mirrors the real one's merged resources.
/// </summary>
internal static class WpfTestHost
{
    // Same list, same order, as App.xaml's merged dictionaries.
    private static readonly string[] ResourceDictionaries =
    {
        "Views/SharedTemplates.xaml",
        "Views/GameBoard/GameBoardResources.xaml",
    };

    private static readonly Lazy<Dispatcher> Host = new(Start);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var path in ResourceDictionaries)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Tavern.Net;component/{path}", UriKind.Absolute),
                });
            }

            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WPF test host" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    /// <summary>Runs <paramref name="work"/> on the WPF thread and returns its result.</summary>
    public static T Invoke<T>(Func<T> work) => Host.Value.Invoke(work);

    public static void Invoke(Action work) => Host.Value.Invoke(work);
}
