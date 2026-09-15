using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// An immutable copy of one card's state at the moment a <see cref="GameSnapshot"/> was taken —
/// deliberately not a reference to the live, still-mutating <see cref="CardInstance"/>, since the
/// whole point is that this value never changes after the fact.
/// </summary>
public sealed record CardSnapshot(
    CardDto Card,
    ZoneType Zone,
    bool IsTapped,
    bool IsFlipped,
    double FieldX,
    double FieldY,
    int Counter,
    bool IsEphemeral,
    bool IsIgnited,
    bool IsImbued,
    bool IsRanged,
    bool IsRooted,
    bool IsWarded)
{
    public static CardSnapshot From(CardInstance instance, ZoneType zone) => new(
        instance.Card,
        zone,
        instance.IsTapped,
        instance.IsFlipped,
        instance.FieldX,
        instance.FieldY,
        instance.Counter,
        instance.IsEphemeral,
        instance.IsIgnited,
        instance.IsImbued,
        instance.IsRanged,
        instance.IsRooted,
        instance.IsWarded);
}
