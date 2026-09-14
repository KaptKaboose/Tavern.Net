using System.Text.Json.Serialization;

namespace Tavern.Net.GameData.Models;

public sealed class CardCost
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public sealed class CardSet
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

public sealed class CardEdition
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("collector_number")]
    public string? CollectorNumber { get; set; }

    [JsonPropertyName("rarity")]
    public int? Rarity { get; set; }

    [JsonPropertyName("illustrator")]
    public string? Illustrator { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("set")]
    public CardSet? Set { get; set; }

    /// <summary>
    /// The other face of this edition, for a genuinely double-faced card (e.g. a Fatestone that
    /// flips into a different named card) — the back face for an edition oriented "front", and
    /// vice versa. Empty for the vast majority of ordinary, single-faced cards; not a generic
    /// "card back" texture (confirmed against the live API — there isn't one exposed here). Each
    /// entry is a full card-shaped object (its own name/effect/etc.) with a single nested
    /// "edition" object carrying its image — not shaped like <see cref="CardEdition"/> itself,
    /// which is why this is its own small type rather than reusing it.
    /// </summary>
    [JsonPropertyName("other_orientations")]
    public List<CardOtherOrientation>? OtherOrientations { get; set; }
}

/// <summary>One entry of <see cref="CardEdition.OtherOrientations"/> — the other face of a double-faced card.</summary>
public sealed class CardOtherOrientation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("effect")]
    public string? Effect { get; set; }

    [JsonPropertyName("edition")]
    public CardOtherOrientationEdition? Edition { get; set; }
}

public sealed class CardOtherOrientationEdition
{
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }
}

public sealed class CardDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("types")]
    public List<string> Types { get; set; } = new();

    [JsonPropertyName("classes")]
    public List<string> Classes { get; set; } = new();

    [JsonPropertyName("subtypes")]
    public List<string> Subtypes { get; set; } = new();

    [JsonPropertyName("elements")]
    public List<string> Elements { get; set; } = new();

    [JsonPropertyName("cost")]
    public CardCost? Cost { get; set; }

    [JsonPropertyName("power")]
    public double? Power { get; set; }

    [JsonPropertyName("life")]
    public double? Life { get; set; }

    [JsonPropertyName("level")]
    public double? Level { get; set; }

    [JsonPropertyName("durability")]
    public double? Durability { get; set; }

    [JsonPropertyName("speed")]
    public bool? Speed { get; set; }

    [JsonPropertyName("effect")]
    public string? Effect { get; set; }

    [JsonPropertyName("effect_raw")]
    public string? EffectRaw { get; set; }

    [JsonPropertyName("flavor")]
    public string? Flavor { get; set; }

    [JsonPropertyName("editions")]
    public List<CardEdition> Editions { get; set; } = new();

    /// <summary>The edition to display/use for artwork — first available edition.</summary>
    [JsonIgnore]
    public CardEdition? PrimaryEdition => Editions.Count > 0 ? Editions[0] : null;

    public bool IsChampionOrRegalia =>
        Types.Any(t => string.Equals(t, "Champion", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(t, "Regalia", StringComparison.OrdinalIgnoreCase));

    public bool IsChampion => Types.Any(t => string.Equals(t, "Champion", StringComparison.OrdinalIgnoreCase));
}

public sealed class SearchCardsResponse
{
    [JsonPropertyName("data")]
    public List<CardDto> Data { get; set; } = new();

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("total_cards")]
    public int TotalCards { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}
