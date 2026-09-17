using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tavern.Net.Decklists;

/// <summary>
/// Persists named decks to a single JSON file under %LocalAppData%\Tavern.Net, shared by every
/// running instance (it's a library — decks you've built should show up everywhere). Which one is
/// "active" — the deck Solo/Online use to jump straight into a game — is instead per-instance (see
/// AppInstanceSlot), stored in its own slot-numbered file, so two copies running side by side (e.g.
/// testing Online against yourself) can each have a different deck selected at once.
/// </summary>
public sealed class DeckStorageService
{
    private sealed class LibraryFile
    {
        public List<SavedDeck> Decks { get; set; } = new();
    }

    private sealed class ActiveDeckFile
    {
        public string? ActiveDeckName { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _libraryFilePath;
    private readonly string _activeDeckFilePath;

    public DeckStorageService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");
        Directory.CreateDirectory(appDataDirectory);
        _libraryFilePath = Path.Combine(appDataDirectory, "decks.json");
        _activeDeckFilePath = Path.Combine(appDataDirectory, $"active-deck-{AppInstanceSlot.Id}.json");
    }

    public IReadOnlyList<SavedDeck> LoadAll() => LoadLibrary().Decks;

    public SavedDeck? GetActiveDeck()
    {
        var activeDeckName = LoadActiveDeck().ActiveDeckName;
        return LoadLibrary().Decks.FirstOrDefault(d => d.Name == activeDeckName);
    }

    /// <summary>Saves (overwriting any existing deck with the same name) and marks it active.</summary>
    public void SaveAndActivate(SavedDeck deck)
    {
        var library = LoadLibrary();
        library.Decks.RemoveAll(d => d.Name == deck.Name);
        library.Decks.Add(deck);
        PersistLibrary(library);

        SetActiveDeck(deck.Name);
    }

    /// <summary>Marks an already-saved deck active, e.g. when the player loads it without re-saving.</summary>
    public void SetActiveDeck(string name)
    {
        PersistActiveDeck(new ActiveDeckFile { ActiveDeckName = name });
    }

    /// <summary>Deletes a saved deck, clearing this instance's active deck too if it was the one
    /// deleted (other instances that had a different deck active are unaffected).</summary>
    public void Delete(string name)
    {
        var library = LoadLibrary();
        if (library.Decks.RemoveAll(d => d.Name == name) > 0)
        {
            PersistLibrary(library);
        }

        var activeDeck = LoadActiveDeck();
        if (activeDeck.ActiveDeckName == name)
        {
            PersistActiveDeck(new ActiveDeckFile());
        }
    }

    private LibraryFile LoadLibrary()
    {
        if (!File.Exists(_libraryFilePath))
        {
            return new LibraryFile();
        }

        try
        {
            var json = File.ReadAllText(_libraryFilePath);
            return JsonSerializer.Deserialize<LibraryFile>(json, JsonOptions) ?? new LibraryFile();
        }
        catch (JsonException)
        {
            return new LibraryFile();
        }
    }

    private void PersistLibrary(LibraryFile library)
    {
        File.WriteAllText(_libraryFilePath, JsonSerializer.Serialize(library, JsonOptions));
    }

    private ActiveDeckFile LoadActiveDeck()
    {
        if (!File.Exists(_activeDeckFilePath))
        {
            return new ActiveDeckFile();
        }

        try
        {
            var json = File.ReadAllText(_activeDeckFilePath);
            return JsonSerializer.Deserialize<ActiveDeckFile>(json, JsonOptions) ?? new ActiveDeckFile();
        }
        catch (JsonException)
        {
            return new ActiveDeckFile();
        }
    }

    private void PersistActiveDeck(ActiveDeckFile activeDeck)
    {
        File.WriteAllText(_activeDeckFilePath, JsonSerializer.Serialize(activeDeck, JsonOptions));
    }
}
