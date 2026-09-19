namespace Tavern.Net.Tests;

public class AppVersionTests
{
    [Fact]
    public void Display_IsADottedVersion_WithoutBuildMetadata()
    {
        // Deliberately not pinned to a specific number, so bumping the csproj's <Version> doesn't
        // break this test.
        Assert.DoesNotContain("+", AppVersion.Display);
        Assert.True(System.Version.TryParse(AppVersion.Display, out _), AppVersion.Display);
    }

    [Fact]
    public void Mismatch_IsNullWhenVersionsMatch_OrTheOpponentHasntGreetedYet()
    {
        Assert.Null(AppVersion.DescribeMismatch("0.1.3", "0.1.3", opponentHasGreeted: true));
        Assert.Null(AppVersion.DescribeMismatch("0.1.3", null, opponentHasGreeted: false));
    }

    [Fact]
    public void Mismatch_NamesBothVersions()
    {
        var message = AppVersion.DescribeMismatch("0.1.3", "0.1.2", opponentHasGreeted: true);

        Assert.Contains("0.1.3", message);
        Assert.Contains("0.1.2", message);
    }

    [Fact]
    public void Mismatch_AGreetingWithNoVersionMeansAnOlderBuild()
    {
        var message = AppVersion.DescribeMismatch("0.1.3", null, opponentHasGreeted: true);

        Assert.Contains("older version", message);
    }
}
