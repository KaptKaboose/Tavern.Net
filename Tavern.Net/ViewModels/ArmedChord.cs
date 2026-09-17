namespace Tavern.Net.ViewModels;

/// <summary>The actions offered by GameBoardViewModel's Actions menu. Shuffle needs no count — it
/// runs the instant it's picked (see GameBoardViewModel.SelectAction) — every other entry moves on
/// to the menu's count-entry view first.</summary>
public enum ArmedChord
{
    None,
    Banish,
    Reveal,
    Give,
    Mill,
    Glimpse,
    Shuffle,
}
