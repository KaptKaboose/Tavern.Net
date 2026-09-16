using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.GameData;

/// <summary>
/// Thin wrapper around the public Grand Archive card API (https://api-docs.gatcg.com).
/// No authentication is required for the endpoints used here.
/// </summary>
public sealed class GrandArchiveApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _imageCacheDirectory;
    private readonly string _cardCacheDirectory;

    public GrandArchiveApiClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.gatcg.com") };
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://api.gatcg.com");
        }

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");

        _imageCacheDirectory = Path.Combine(appDataDirectory, "ImageCache");
        Directory.CreateDirectory(_imageCacheDirectory);

        _cardCacheDirectory = Path.Combine(appDataDirectory, "CardCache");
        Directory.CreateDirectory(_cardCacheDirectory);
    }

    /// <summary>
    /// Searches for cards by name, disk-caching the response so a decklist that references the
    /// same card again — the common case, since most decks run multiple copies — and re-imports
    /// of the same list don't re-hit the network for every line.
    /// </summary>
    public async Task<SearchCardsResponse> SearchCardsAsync(
        string name,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var cacheFile = GetCardCacheFilePath($"search|{name}|{page}|{pageSize}");
        if (File.Exists(cacheFile))
        {
            var cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            var cachedResponse = JsonSerializer.Deserialize<SearchCardsResponse>(cachedJson, JsonOptions);
            if (cachedResponse is not null)
            {
                return cachedResponse;
            }
        }

        var query = $"name={Uri.EscapeDataString(name)}&page={page}&page_size={pageSize}";

        var response = await _http.GetFromJsonAsync<SearchCardsResponse>(
            $"/cards/search?{query}", JsonOptions, cancellationToken) ?? new SearchCardsResponse();

        await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(response, JsonOptions), cancellationToken);
        return response;
    }

    /// <summary>
    /// Fetches every Token card in the game (the whole catalog is small — a few dozen — so this is
    /// a single page, disk-cached like <see cref="SearchCardsAsync"/>) for the Tokens zone's
    /// always-available browsable pile.
    /// </summary>
    public async Task<IReadOnlyList<CardDto>> GetTokensAsync(CancellationToken cancellationToken = default)
    {
        var cacheFile = GetCardCacheFilePath("tokens|catalog");
        if (File.Exists(cacheFile))
        {
            var cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            var cachedResponse = JsonSerializer.Deserialize<SearchCardsResponse>(cachedJson, JsonOptions);
            if (cachedResponse is not null)
            {
                return cachedResponse.Data;
            }
        }

        var response = await _http.GetFromJsonAsync<SearchCardsResponse>(
            "/cards/search?type=token&page_size=100", JsonOptions, cancellationToken) ?? new SearchCardsResponse();

        await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(response, JsonOptions), cancellationToken);
        return response.Data;
    }

    /// <summary>
    /// Writes a card straight into the by-slug cache <see cref="GetCardBySlugAsync"/> reads from,
    /// without a network call — used when a card resolved via <see cref="SearchCardsAsync"/> (a
    /// different cache key) is about to be referenced by slug later, e.g. DeckImportViewModel.SaveDeck
    /// caching each saved deck's cards so the very first reload doesn't re-fetch them one by one.
    /// </summary>
    public void CacheCard(CardDto card)
    {
        if (string.IsNullOrEmpty(card.Slug))
        {
            return;
        }

        var cacheFile = GetCardCacheFilePath($"card|{card.Slug}");
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(card, JsonOptions));
    }

    private string GetCardCacheFilePath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        return Path.Combine(_cardCacheDirectory, $"{hash}.json");
    }

    /// <summary>Deletes all cached card data and card images, so the next lookup re-fetches from the API.</summary>
    public void ClearCache()
    {
        ClearDirectory(_cardCacheDirectory);
        ClearDirectory(_imageCacheDirectory);
    }

    private static void ClearDirectory(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            File.Delete(file);
        }
    }

    /// <summary>Fetches the full card by slug (GET /cards/{slug}), disk-cached like <see cref="SearchCardsAsync"/>.</summary>
    public async Task<CardDto?> GetCardBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var cacheFile = GetCardCacheFilePath($"card|{slug}");
        if (File.Exists(cacheFile))
        {
            var cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            var cached = JsonSerializer.Deserialize<CardDto>(cachedJson, JsonOptions);
            if (cached is not null)
            {
                return cached;
            }
        }

        var response = await _http.GetAsync($"/cards/{Uri.EscapeDataString(slug)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var card = await response.Content.ReadFromJsonAsync<CardDto>(JsonOptions, cancellationToken);
        if (card is not null)
        {
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(card, JsonOptions), cancellationToken);
        }

        return card;
    }

    /// <summary>
    /// Downloads (and disk-caches) a card image, returning a local file path suitable for loading
    /// into a WPF BitmapImage. <paramref name="imagePath"/> is an API image path, e.g.
    /// "/cards/images/f6p4dnamcf.jpg". <paramref name="rounded"/> requests the API's own
    /// pre-rounded-corner rendering (the "rounded" query param on /cards/images/{filename}) and is
    /// cached under a distinct filename, since it's a different image from the square-cornered one.
    /// </summary>
    public async Task<string?> GetCardImagePathAsync(string imagePath, bool rounded = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(imagePath);
        if (rounded)
        {
            fileName = $"{Path.GetFileNameWithoutExtension(fileName)}.rounded{Path.GetExtension(fileName)}";
        }

        var localPath = Path.Combine(_imageCacheDirectory, fileName);

        if (File.Exists(localPath))
        {
            return localPath;
        }

        var requestPath = rounded ? $"{imagePath}?rounded=true" : imagePath;
        var response = await _http.GetAsync(requestPath, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var fileStream = File.Create(localPath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
        return localPath;
    }
}
