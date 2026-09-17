using Tavern.Net.Game;

namespace Tavern.Net.ViewModels;

/// <summary>One entry in an online game's merged Play Log — GameBoardViewModel.MergedLog interleaves
/// both players' MajorEvents by Timestamp; IsOwn distinguishes "You" from "Opp" in the log's own
/// item template.</summary>
public sealed record MergedLogEntry(bool IsOwn, MajorEvent Event);
