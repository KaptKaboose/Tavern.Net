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
        var error = WpfTestHost.Invoke(() =>
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
        var failures = WpfTestHost.Invoke(() =>
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
