namespace Tavern.Net.Game;

/// <summary>
/// The turn (1-based, matching GameStats.Log's display convention) a Champion level was first
/// reached — recorded once per distinct level and never overwritten, so re-leveling back down and
/// up again doesn't reset the original milestone. See GameSession's RecordChampionLevelReached.
/// </summary>
public sealed record ChampionLevelMilestone(double Level, int Turn);
