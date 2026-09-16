using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tavern.Net.Game;

/// <summary>
/// Persists named game saves to a single JSON file under %LocalAppData%\Tavern.Net. Unlike
/// DeckStorageService there's no "active" concept — View Game always shows the full list to pick
/// from, rather than Solo silently resuming one.
/// </summary>
public sealed class GameStorageService
{
    private sealed class StorageFile
    {
        public List<SavedGame> Games { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public GameStorageService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");
        Directory.CreateDirectory(appDataDirectory);
        _filePath = Path.Combine(appDataDirectory, "games.json");
    }

    public IReadOnlyList<SavedGame> LoadAll() => Load().Games;

    /// <summary>Saves (overwriting any existing save with the same name).</summary>
    public void Save(SavedGame game)
    {
        var file = Load();
        file.Games.RemoveAll(g => g.Name == game.Name);
        file.Games.Add(game);
        Persist(file);
    }

    public void Delete(string name)
    {
        var file = Load();
        if (file.Games.RemoveAll(g => g.Name == name) > 0)
        {
            Persist(file);
        }
    }

    private StorageFile Load()
    {
        if (!File.Exists(_filePath))
        {
            return new StorageFile();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<StorageFile>(json, JsonOptions) ?? new StorageFile();
        }
        catch (JsonException)
        {
            return new StorageFile();
        }
    }

    private void Persist(StorageFile file)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(file, JsonOptions));
    }
}
