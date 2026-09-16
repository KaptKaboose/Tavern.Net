using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tavern.Net.Decklists;

/// <summary>
/// Persists named decks to a single JSON file under %LocalAppData%\Tavern.Net, alongside which one
/// is "active" — the deck Solo uses to jump straight into a game without going through the Change
/// Deck screen again.
/// </summary>
public sealed class DeckStorageService
{
    private sealed class StorageFile
    {
        public string? ActiveDeckName { get; set; }

        public List<SavedDeck> Decks { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public DeckStorageService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");
        Directory.CreateDirectory(appDataDirectory);
        _filePath = Path.Combine(appDataDirectory, "decks.json");
    }

    public IReadOnlyList<SavedDeck> LoadAll() => Load().Decks;

    public SavedDeck? GetActiveDeck()
    {
        var file = Load();
        return file.Decks.FirstOrDefault(d => d.Name == file.ActiveDeckName);
    }

    /// <summary>Saves (overwriting any existing deck with the same name) and marks it active.</summary>
    public void SaveAndActivate(SavedDeck deck)
    {
        var file = Load();
        file.Decks.RemoveAll(d => d.Name == deck.Name);
        file.Decks.Add(deck);
        file.ActiveDeckName = deck.Name;
        Persist(file);
    }

    /// <summary>Marks an already-saved deck active, e.g. when the player loads it without re-saving.</summary>
    public void SetActiveDeck(string name)
    {
        var file = Load();
        if (file.Decks.Any(d => d.Name == name))
        {
            file.ActiveDeckName = name;
            Persist(file);
        }
    }

    /// <summary>Deletes a saved deck, clearing ActiveDeckName too if it was the one deleted.</summary>
    public void Delete(string name)
    {
        var file = Load();
        if (file.Decks.RemoveAll(d => d.Name == name) > 0)
        {
            if (file.ActiveDeckName == name)
            {
                file.ActiveDeckName = null;
            }

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
