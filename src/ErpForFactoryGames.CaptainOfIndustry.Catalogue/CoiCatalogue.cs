using System.Text.Json.Serialization;

namespace ErpForFactoryGames.CaptainOfIndustry.Catalogue;

/// <summary>
/// Captain of Industry's product, recipe and building catalogue, as read from a
/// local game installation.
/// </summary>
/// <remarks>
/// The JSON property names are a wire contract with the ERP planner. Renaming
/// one is a breaking schema change — note in particular that
/// <see cref="Products"/> serialises as <c>items</c> for historical reasons.
/// </remarks>
public sealed record CoiCatalogue
{
    /// <summary>Version of the library that produced this catalogue.</summary>
    [JsonPropertyName("extractorVersion")]
    public string ExtractorVersion { get; init; } = "";

    /// <summary>Assembly version of the game the catalogue was read from.</summary>
    [JsonPropertyName("coiVersion")]
    public string GameVersion { get; init; } = "";

    [JsonPropertyName("extractedAt")]
    public DateTimeOffset ExtractedAt { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<CoiProduct> Products { get; init; } = [];

    [JsonPropertyName("recipes")]
    public IReadOnlyList<CoiRecipe> Recipes { get; init; } = [];

    [JsonPropertyName("buildings")]
    public IReadOnlyList<CoiBuilding> Buildings { get; init; } = [];

    /// <summary>
    /// Non-fatal problems hit while reading. A catalogue with warnings is still
    /// usable — prototype registration can stop partway and still leave most of
    /// the database populated.
    /// </summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public CoiProduct? Product(string id) => Products.FirstOrDefault(p => p.Id == id);

    public CoiRecipe? Recipe(string id) => Recipes.FirstOrDefault(r => r.Id == id);

    public CoiBuilding? Building(string id) => Buildings.FirstOrDefault(b => b.Id == id);

    /// <summary>Recipes that produce the given product.</summary>
    public IEnumerable<CoiRecipe> RecipesProducing(string productId) =>
        Recipes.Where(r => r.Outputs.Any(o => o.ProductId == productId));

    /// <summary>Recipes that consume the given product.</summary>
    public IEnumerable<CoiRecipe> RecipesConsuming(string productId) =>
        Recipes.Where(r => r.Inputs.Any(i => i.ProductId == productId));

    public override string ToString() =>
        $"CoI {GameVersion}: {Products.Count} products, {Recipes.Count} recipes, "
        + $"{Buildings.Count} buildings, {Warnings.Count} warnings";
}

public sealed record CoiProduct
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>The prototype's kind — <c>Loose</c>, <c>Fluid</c>, <c>Molten</c>, and so on.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";

    [JsonPropertyName("isStorable")] public bool IsStorable { get; init; }
    [JsonPropertyName("isWaste")] public bool IsWaste { get; init; }
    [JsonPropertyName("radioactivity")] public int Radioactivity { get; init; }

    public override string ToString() => $"{Id} ({Kind})";
}

public sealed record CoiRecipe
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>Id of the machine that runs this recipe, if one claimed it.</summary>
    [JsonPropertyName("building")] public string? Building { get; init; }

    /// <summary>Duration in game ticks. See <see cref="DurationSeconds"/>.</summary>
    [JsonPropertyName("durationTicks")] public int DurationTicks { get; init; }

    [JsonPropertyName("inputs")] public IReadOnlyList<CoiRecipeProduct> Inputs { get; init; } = [];
    [JsonPropertyName("outputs")] public IReadOnlyList<CoiRecipeProduct> Outputs { get; init; } = [];

    /// <summary>Captain of Industry runs at a fixed 10 ticks per second.</summary>
    public const int TicksPerSecond = 10;

    [JsonIgnore]
    public double DurationSeconds => DurationTicks / (double)TicksPerSecond;

    /// <summary>Units of <paramref name="productId"/> produced per minute at full uptime.</summary>
    public double OutputPerMinute(string productId)
    {
        if (DurationTicks <= 0) return 0;
        var quantity = Outputs.Where(o => o.ProductId == productId).Sum(o => o.Quantity);
        return quantity * 60.0 / DurationSeconds;
    }

    /// <summary>Units of <paramref name="productId"/> consumed per minute at full uptime.</summary>
    public double InputPerMinute(string productId)
    {
        if (DurationTicks <= 0) return 0;
        var quantity = Inputs.Where(i => i.ProductId == productId).Sum(i => i.Quantity);
        return quantity * 60.0 / DurationSeconds;
    }

    public override string ToString() => $"{Id} ({DurationSeconds:0.#}s)";
}

public sealed record CoiRecipeProduct
{
    [JsonPropertyName("productId")] public string ProductId { get; init; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; init; }

    public override string ToString() => $"{Quantity}× {ProductId}";
}

public sealed record CoiBuilding
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("electricityKw")] public int ElectricityKw { get; init; }

    /// <summary>Ids of the recipes this building can run.</summary>
    [JsonPropertyName("recipes")] public IReadOnlyList<string> Recipes { get; init; } = [];

    public override string ToString() => $"{Id} ({Recipes.Count} recipes)";
}
