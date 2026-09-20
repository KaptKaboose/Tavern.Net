using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Life and damage-dealt adjustments.</summary>
public sealed partial class GameSession
{
    /// <param name="trackRecovery">Whether a positive delta counts toward LifeRecoveredCount —
    /// true for the Life panel's own +/- buttons, false for a Champion level-up/down's automatic
    /// life shift, which isn't the player "recovering" anything.</param>
    public void AdjustLife(Player player, int delta, bool trackRecovery = true)
    {
        var newLife = Math.Max(0, player.Life + delta);
        var actualDelta = newLife - player.Life;
        if (actualDelta == 0)
        {
            // Already at 0 and dropping further (or an already-clamped attempt repeats) — nothing
            // actually changed, so skip the log/MajorEvent rather than recording a "+0" no-op.
            return;
        }

        player.Life = newLife;
        if (trackRecovery && actualDelta > 0)
        {
            player.Stats.LifeRecoveredCount += actualDelta;
        }

        player.Stats.Log($"Life changed by {actualDelta:+0;-0} to {player.Life}.");
        RecordLifeOrDamageChange(player, MajorEventKind.LifeChanged, "Life", actualDelta);
    }

    /// <summary>Adjusts the running damage-dealt tally (a manual count, since goldfishing has no
    /// opponent board to compute it from) — clamped at 0 so an over-eager decrease can't go
    /// negative.</summary>
    public void AdjustDamageDealt(Player player, int delta)
    {
        player.Stats.DamageDealtCount = Math.Max(0, player.Stats.DamageDealtCount + delta);
        player.Stats.Log($"Damage dealt changed by {delta:+0;-0} to {player.Stats.DamageDealtCount}.");
        RecordLifeOrDamageChange(player, MajorEventKind.DamageDealt, "Damage dealt", delta);
    }
}
