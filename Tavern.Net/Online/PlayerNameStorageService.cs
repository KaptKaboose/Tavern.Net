using System.IO;
using System.Text.Json;

namespace Tavern.Net.Online;

/// <summary>Remembers the player's chosen Online display name across sessions — saved once a game
/// actually starts (not on every keystroke, so an abandoned lobby doesn't overwrite a good name with
/// a typo), reloaded as the default the next time the lobby opens. Per-instance (see
/// AppInstanceSlot), so two copies running side by side each keep their own name instead of one
/// overwriting the other's.</summary>
public sealed class PlayerNameStorageService
{
    private sealed class StorageFile
    {
        public string? PlayerName { get; set; }
    }

    private readonly string _filePath;

    public PlayerNameStorageService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");
        Directory.CreateDirectory(appDataDirectory);
        _filePath = Path.Combine(appDataDirectory, $"online-settings-{AppInstanceSlot.Id}.json");
    }

    public string? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<StorageFile>(json)?.PlayerName;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(string playerName)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(new StorageFile { PlayerName = playerName }));
    }
}
