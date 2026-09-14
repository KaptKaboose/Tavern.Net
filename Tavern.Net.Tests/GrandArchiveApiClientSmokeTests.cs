using Tavern.Net.GameData;

namespace Tavern.Net.Tests;

/// <summary>
/// Hits the real, live api.gatcg.com API. These are smoke tests, not
/// deterministic unit tests — they confirm our DTOs still match the
/// live API shape.
/// </summary>
public class GrandArchiveApiClientSmokeTests
{
    [Fact]
    public async Task SearchCardsAsync_ReturnsRealCardData()
    {
        var client = new GrandArchiveApiClient();

        var result = await client.SearchCardsAsync("Silvie", pageSize: 5);

        Assert.NotEmpty(result.Data);
        Assert.Contains(result.Data, c => c.Name.Contains("Silvie", StringComparison.OrdinalIgnoreCase));
        var card = result.Data[0];
        Assert.False(string.IsNullOrWhiteSpace(card.Slug));
        Assert.NotEmpty(card.Editions);
        Assert.False(string.IsNullOrWhiteSpace(card.PrimaryEdition!.Image));
    }

    [Fact]
    public async Task GetTokensAsync_ReturnsRealTokenDataWithReferencedByLinks()
    {
        var client = new GrandArchiveApiClient();

        var tokens = await client.GetTokensAsync();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, t => Assert.True(t.IsToken));
        // "Training Dummy" is referenced_by "Dummy Trainer" (a SUMMON link) — confirms the
        // referenced_by field actually round-trips through CardDto, not just that it deserializes.
        var trainingDummy = Assert.Single(tokens, t => t.Name == "Training Dummy");
        Assert.Contains(trainingDummy.ReferencedBy, r => r.Name == "Dummy Trainer");
    }

    [Fact]
    public async Task GetCardImagePathAsync_DownloadsAndCachesImage()
    {
        var client = new GrandArchiveApiClient();
        var search = await client.SearchCardsAsync("Silvie", pageSize: 1);
        var imagePath = search.Data[0].PrimaryEdition!.Image!;

        var localPath = await client.GetCardImagePathAsync(imagePath);

        Assert.NotNull(localPath);
        Assert.True(File.Exists(localPath));
        Assert.True(new FileInfo(localPath!).Length > 0);
    }
}
