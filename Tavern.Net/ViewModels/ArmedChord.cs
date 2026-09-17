namespace Tavern.Net.ViewModels;

/// <summary>Which of the arm-then-digit keyboard chords (Banish, Reveal, Give, Mill, Glimpse) is
/// currently waiting on its count digit — see GameBoardViewModel.HandleKey. Replaces what used to
/// be a single-purpose _banishArmed bool now that there's more than one of these.</summary>
public enum ArmedChord
{
    None,
    Banish,
    Reveal,
    Give,
    Mill,
    Glimpse,
}
