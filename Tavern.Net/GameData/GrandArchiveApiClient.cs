using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
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

    public GrandArchiveApiClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.gatcg.com") };
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://api.gatcg.com");
        }

        _imageCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net",
            "ImageCache");
        Directory.CreateDirectory(_imageCacheDirectory);
    }

    public async Task<SearchCardsResponse> SearchCardsAsync(
        string name,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = $"name={Uri.EscapeDataString(name)}&page={page}&page_size={pageSize}";

        var response = await _http.GetFromJsonAsync<SearchCardsResponse>(
            $"/cards/search?{query}", JsonOptions, cancellationToken);

        return response ?? new SearchCardsResponse();
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
