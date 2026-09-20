using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Tavern.Net.Tests;

/// <summary>
/// Loads every view (and the app-level resource dictionaries) once, on a WPF UI thread, so a XAML
/// mistake — a missing StaticResource, a bad property, an unresolved type — fails a test instead of
/// only showing up as a crash the first time that screen opens. Bindings aren't exercised (there is no
/// DataContext); this is about the XAML itself parsing and its resources resolving.
/// </summary>
public class ViewSmokeTests
{
    // Same list, same order, as App.xaml's merged dictionaries.
    private static readonly string[] ResourceDictionaries =
    {
        "Views/SharedTemplates.xaml",
        "Views/GameBoard/GameBoardResources.xaml",
    };

    /// <summary>One shared STA thread with a dispatcher: WPF objects belong to the thread that made
    /// them, and an Application can only exist once per process.</summary>
    private static class WpfHost
    {
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
            { IsBackground = true, Name = "WPF smoke-test host" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return dispatcher!;
        }

        public static T Invoke<T>(Func<T> work) => Host.Value.Invoke(work);
    }

    public static IEnumerable<object[]> ViewTypes() =>
        typeof(Tavern.Net.App).Assembly.GetTypes()
            .Where(t => typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract && t.Namespace?.StartsWith("Tavern.Net.Views") == true)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t.FullName! });

    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void EveryViewLoads(string typeName)
    {
        var type = typeof(Tavern.Net.App).Assembly.GetType(typeName)!;
        var error = WpfHost.Invoke(() =>
        {
            try
            {
                var view = (UserControl)Activator.CreateInstance(type)!;
                view.Measure(new Size(1600, 950));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.True(error is null, $"{typeName} failed to load: {error}");
    }

    [Fact]
    public void EveryDataTemplateInTheAppResourcesInstantiates()
    {
        var failures = WpfHost.Invoke(() =>
        {
            var found = new List<string>();
            foreach (var entry in Application.Current.Resources.MergedDictionaries.SelectMany(Flatten))
            {
                if (entry.Value is DataTemplate template)
                {
                    try
                    {
                        template.LoadContent();
                    }
                    catch (Exception ex)
                    {
                        found.Add($"{entry.Key}: {ex.GetBaseException().Message}");
                    }
                }
            }

            return found;
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<KeyValuePair<object, object?>> Flatten(ResourceDictionary dictionary)
    {
        foreach (var child in dictionary.MergedDictionaries)
        {
            foreach (var entry in Flatten(child))
            {
                yield return entry;
            }
        }

        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            yield return new KeyValuePair<object, object?>(entry.Key, entry.Value);
        }
    }
}
