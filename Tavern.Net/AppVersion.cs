using System.Reflection;

namespace Tavern.Net;

/// <summary>
/// The running app's version — the csproj's single <c>&lt;Version&gt;</c>, read back from the
/// assembly so what's shown in the app is always what was actually built. Also the one place that
/// decides whether two players' versions are compatible for online play.
/// </summary>
public static class AppVersion
{
    /// <summary>E.g. "0.1.3". The SDK appends "+&lt;commit&gt;" to the informational version when
    /// it can see git; that's build metadata, not part of the version players compare, so it's cut.</summary>
    public static string Display { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>The lobby's warning when the two players' versions differ, or null when they match
    /// (or the opponent's isn't known yet). A null <paramref name="theirs"/> after they've said hello
    /// means a build from before versions were sent at all.</summary>
    public static string? DescribeMismatch(string ours, string? theirs, bool opponentHasGreeted)
    {
        if (!opponentHasGreeted || string.Equals(ours, theirs, StringComparison.Ordinal))
        {
            return null;
        }

        var theirDescription = theirs is null ? "an older version" : $"version {theirs}";
        return $"Version mismatch: you're on {ours} and your opponent is on {theirDescription}. " +
               "You both need the same version to play online — one of you needs to update.";
    }
}
