using System.IO;

namespace Tavern.Net;

/// <summary>
/// Claims a small numeric "slot" (0, 1, 2, ...) unique among concurrently-running Tavern.Net
/// processes on this machine, by holding an exclusive lock on a per-slot file for the process's
/// lifetime. Lets two instances run side by side (e.g. testing Online against yourself) without
/// sharing per-instance state like the active deck or Online player name, while still sharing the
/// actual saved-deck/game libraries, which live in slot-independent files (see DeckStorageService,
/// GameStorageService). The slot resets each run — it's "whichever copies happen to be open right
/// now," not a permanent identity — which is fine since a slot still remembers its own settings
/// across that copy's own restarts as long as it's the only one running, the common case.
/// </summary>
public static class AppInstanceSlot
{
    private static FileStream? _lockStream;

    public static int Id { get; private set; }

    /// <summary>Call once, as early as possible at startup (before any storage service is
    /// constructed) — everything else just reads Id afterward.</summary>
    public static void Claim()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavern.Net");
        Directory.CreateDirectory(appDataDirectory);

        for (var slot = 0; slot < 64; slot++)
        {
            var lockPath = Path.Combine(appDataDirectory, $"instance-{slot}.lock");
            try
            {
                // FileShare.None: fails if another process already holds this same file open —
                // exactly the signal needed to move on to the next slot. Never disposed, so the
                // lock holds for the rest of this process's life (released automatically on exit).
                _lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Id = slot;
                return;
            }
            catch (IOException)
            {
                // Already claimed by another running instance — try the next slot.
            }
        }

        // Wildly unlikely (64+ concurrent instances) — fall back to slot 0 rather than crash; worst
        // case is shared state with whichever instance already holds it.
        Id = 0;
    }
}
