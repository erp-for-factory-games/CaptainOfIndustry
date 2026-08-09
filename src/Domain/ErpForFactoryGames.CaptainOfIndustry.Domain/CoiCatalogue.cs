namespace ErpForFactoryGames.CaptainOfIndustry.Domain;

/// <summary>
/// Captain of Industry's products, recipes and buildings — everything the
/// planner needs to reason about what the game can make.
/// </summary>
/// <remarks>
/// Types here carry a <c>Coi</c> prefix on purpose. The planner consumes several
/// game packages at once, so unprefixed <c>Recipe</c> / <c>Product</c> would
/// collide across them and force using-aliases at every call site.
/// </remarks>
public sealed record CoiCatalogue
{
    /// <summary>Version of the game this catalogue was read from.</summary>
    public string GameVersion { get; init; } = "";

    /// <summary>When the catalogue was read.</summary>
    public DateTimeOffset ReadAt { get; init; }

    public IReadOnlyList<CoiProduct> Products { get; init; } = [];

    public IReadOnlyList<CoiRecipe> Recipes { get; init; } = [];

    public IReadOnlyList<CoiBuilding> Buildings { get; init; } = [];

    /// <summary>
    /// Non-fatal problems encountered while reading. A catalogue with warnings is
    /// still usable — prototype registration can stop partway and leave the parts
    /// the planner needs fully populated.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public CoiProduct? Product(string id) => Products.FirstOrDefault(p => p.Id == id);

    public CoiRecipe? Recipe(string id) => Recipes.FirstOrDefault(r => r.Id == id);

    public CoiBuilding? Building(string id) => Buildings.FirstOrDefault(b => b.Id == id);

    /// <summary>Every recipe that yields the given product.</summary>
    public IEnumerable<CoiRecipe> RecipesProducing(string productId) =>
        Recipes.Where(r => r.Outputs.Any(o => o.ProductId == productId));

    /// <summary>Every recipe that consumes the given product.</summary>
    public IEnumerable<CoiRecipe> RecipesConsuming(string productId) =>
        Recipes.Where(r => r.Inputs.Any(i => i.ProductId == productId));

    /// <summary>
    /// Product ids referenced by a recipe that no product declares. Non-empty
    /// means the read was incomplete — worth surfacing rather than planning on.
    /// </summary>
    public IReadOnlyList<string> DanglingProductReferences()
    {
        var known = Products.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        return Recipes
            .SelectMany(r => r.Inputs.Concat(r.Outputs))
            .Select(p => p.ProductId)
            .Where(id => !string.IsNullOrEmpty(id) && !known.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    public override string ToString() =>
        $"Captain of Industry {GameVersion}: {Products.Count} products, "
        + $"{Recipes.Count} recipes, {Buildings.Count} buildings, {Warnings.Count} warnings";
}

/// <summary>A material, fluid or other item the factory moves around.</summary>
public sealed record CoiProduct
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>The prototype kind — <c>Loose</c>, <c>Fluid</c>, <c>Molten</c> and so on.</summary>
    public string Kind { get; init; } = "";

    public bool IsStorable { get; init; }
    public bool IsWaste { get; init; }
    public int Radioactivity { get; init; }

    public override string ToString() => $"{Id} ({Kind})";
}

/// <summary>A conversion of input products into output products over a duration.</summary>
public sealed record CoiRecipe
{
    /// <summary>Captain of Industry runs on a fixed 10-tick second.</summary>
    public const int TicksPerSecond = 10;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Id of the machine that runs this recipe, when one claims it.</summary>
    public string? BuildingId { get; init; }

    public int DurationTicks { get; init; }

    public IReadOnlyList<CoiRecipeProduct> Inputs { get; init; } = [];
    public IReadOnlyList<CoiRecipeProduct> Outputs { get; init; } = [];

    public double DurationSeconds => DurationTicks / (double)TicksPerSecond;

    /// <summary>Units of <paramref name="productId"/> produced per minute at full uptime.</summary>
    public double OutputPerMinute(string productId) =>
        PerMinute(Outputs, productId);

    /// <summary>Units of <paramref name="productId"/> consumed per minute at full uptime.</summary>
    public double InputPerMinute(string productId) =>
        PerMinute(Inputs, productId);

    private double PerMinute(IReadOnlyList<CoiRecipeProduct> side, string productId)
    {
        // A zero duration would be a division by zero, and means the prototype
        // never registered a duration — treat as "no throughput" rather than
        // infinite, which would quietly poison any plan built on it.
        if (DurationTicks <= 0) return 0;

        var quantity = side.Where(p => p.ProductId == productId).Sum(p => p.Quantity);
        return quantity * 60.0 / DurationSeconds;
    }

    public override string ToString() => $"{Id} ({DurationSeconds:0.#}s)";
}

/// <summary>A quantity of a product on one side of a recipe.</summary>
public sealed record CoiRecipeProduct
{
    public string ProductId { get; init; } = "";
    public int Quantity { get; init; }

    public override string ToString() => $"{Quantity}× {ProductId}";
}

/// <summary>A machine that can run recipes.</summary>
public sealed record CoiBuilding
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int ElectricityKw { get; init; }

    /// <summary>Ids of the recipes this building can run.</summary>
    public IReadOnlyList<string> RecipeIds { get; init; } = [];

    public override string ToString() => $"{Id} ({RecipeIds.Count} recipes)";
}
