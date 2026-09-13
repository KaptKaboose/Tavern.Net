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

    public async Task<CardDto?> GetCardBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/cards/{Uri.EscapeDataString(slug)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<CardDto>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Downloads (and disk-caches) a card image, returning a local file path suitable
    /// for loading into a WPF BitmapImage. <paramref name="imagePath"/> is the API's
    /// edition.image value, e.g. "/cards/images/f6p4dnamcf.jpg".
    /// </summary>
    public async Task<string?> GetCardImagePathAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(imagePath);
        var localPath = Path.Combine(_imageCacheDirectory, fileName);

        if (File.Exists(localPath))
        {
            return localPath;
        }

        var response = await _http.GetAsync(imagePath, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var fileStream = File.Create(localPath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
        return localPath;
    }
}
