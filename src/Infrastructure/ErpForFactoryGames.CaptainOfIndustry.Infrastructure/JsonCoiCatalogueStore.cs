using System.Text.Json;
using System.Text.Json.Serialization;
using ErpForFactoryGames.CaptainOfIndustry.Application;
using ErpForFactoryGames.CaptainOfIndustry.Domain;

namespace ErpForFactoryGames.CaptainOfIndustry.Infrastructure;

/// <summary>
/// Persists a catalogue as JSON, so downstream consumers never need the game.
/// </summary>
/// <remarks>
/// The on-disk shape is a wire contract with catalogues written by earlier
/// versions, and two of its keys do not match their domain property:
/// <c>items</c> holds products, and <c>coiVersion</c> holds the game version.
/// That mismatch is exactly why the DTO lives here rather than in the domain —
/// the domain gets to be named sensibly, and the awkwardness stays at the edge.
/// </remarks>
public sealed class JsonCoiCatalogueStore : ICoiCatalogueStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErpForFactoryGames",
        "coi-catalogue.json");

    public void Save(CoiCatalogue catalogue, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, CatalogueDto.From(catalogue), Options);
    }

    public CoiCatalogue Load(string path)
    {
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<CatalogueDto>(stream, Options)
                  ?? throw new CoiCatalogueException($"Catalogue JSON at '{path}' deserialised to null.");

        return dto.ToDomain();
    }

    /// <summary>Serialises to a string. Handy for tests and for piping.</summary>
    public static string Serialize(CoiCatalogue catalogue) =>
        JsonSerializer.Serialize(CatalogueDto.From(catalogue), Options);

    public static CoiCatalogue Deserialize(string json) =>
        (JsonSerializer.Deserialize<CatalogueDto>(json, Options)
         ?? throw new CoiCatalogueException("Catalogue JSON deserialised to null.")).ToDomain();

    // -----------------------------------------------------------------------
    // Wire shape. Changing a JsonPropertyName here is a breaking schema change.
    // -----------------------------------------------------------------------

    internal sealed class CatalogueDto
    {
        [JsonPropertyName("extractorVersion")] public string ExtractorVersion { get; set; } = "";
        [JsonPropertyName("coiVersion")] public string CoiVersion { get; set; } = "";
        [JsonPropertyName("extractedAt")] public DateTimeOffset ExtractedAt { get; set; }
        [JsonPropertyName("items")] public List<ProductDto> Items { get; set; } = [];
        [JsonPropertyName("recipes")] public List<RecipeDto> Recipes { get; set; } = [];
        [JsonPropertyName("buildings")] public List<BuildingDto> Buildings { get; set; } = [];
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];

        public static CatalogueDto From(CoiCatalogue c) => new()
        {
            ExtractorVersion = typeof(CatalogueDto).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            CoiVersion = c.GameVersion,
            ExtractedAt = c.ReadAt,
            Items = c.Products.Select(ProductDto.From).ToList(),
            Recipes = c.Recipes.Select(RecipeDto.From).ToList(),
            Buildings = c.Buildings.Select(BuildingDto.From).ToList(),
            Warnings = c.Warnings.ToList(),
        };

        public CoiCatalogue ToDomain() => new()
        {
            GameVersion = CoiVersion,
            ReadAt = ExtractedAt,
            Products = Items.Select(i => i.ToDomain()).ToList(),
            Recipes = Recipes.Select(r => r.ToDomain()).ToList(),
            Buildings = Buildings.Select(b => b.ToDomain()).ToList(),
            Warnings = Warnings,
        };
    }

    internal sealed class ProductDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("isStorable")] public bool IsStorable { get; set; }
        [JsonPropertyName("isWaste")] public bool IsWaste { get; set; }
        [JsonPropertyName("radioactivity")] public int Radioactivity { get; set; }

        public static ProductDto From(CoiProduct p) => new()
        {
            Id = p.Id, Name = p.Name, Kind = p.Kind,
            IsStorable = p.IsStorable, IsWaste = p.IsWaste, Radioactivity = p.Radioactivity,
        };

        public CoiProduct ToDomain() => new()
        {
            Id = Id, Name = Name, Kind = Kind,
            IsStorable = IsStorable, IsWaste = IsWaste, Radioactivity = Radioactivity,
        };
    }

    internal sealed class RecipeDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("building")] public string? Building { get; set; }
        [JsonPropertyName("durationTicks")] public int DurationTicks { get; set; }
        [JsonPropertyName("inputs")] public List<RecipeProductDto> Inputs { get; set; } = [];
        [JsonPropertyName("outputs")] public List<RecipeProductDto> Outputs { get; set; } = [];

        public static RecipeDto From(CoiRecipe r) => new()
        {
            Id = r.Id, Name = r.Name, Building = r.BuildingId, DurationTicks = r.DurationTicks,
            Inputs = r.Inputs.Select(RecipeProductDto.From).ToList(),
            Outputs = r.Outputs.Select(RecipeProductDto.From).ToList(),
        };

        public CoiRecipe ToDomain() => new()
        {
            Id = Id, Name = Name, BuildingId = Building, DurationTicks = DurationTicks,
            Inputs = Inputs.Select(i => i.ToDomain()).ToList(),
            Outputs = Outputs.Select(o => o.ToDomain()).ToList(),
        };
    }

    internal sealed class RecipeProductDto
    {
        [JsonPropertyName("productId")] public string ProductId { get; set; } = "";
        [JsonPropertyName("quantity")] public int Quantity { get; set; }

        public static RecipeProductDto From(CoiRecipeProduct p) =>
            new() { ProductId = p.ProductId, Quantity = p.Quantity };

        public CoiRecipeProduct ToDomain() => new() { ProductId = ProductId, Quantity = Quantity };
    }

    internal sealed class BuildingDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("electricityKw")] public int ElectricityKw { get; set; }
        [JsonPropertyName("recipes")] public List<string> Recipes { get; set; } = [];

        public static BuildingDto From(CoiBuilding b) => new()
        {
            Id = b.Id, Name = b.Name, ElectricityKw = b.ElectricityKw,
            Recipes = b.RecipeIds.ToList(),
        };

        public CoiBuilding ToDomain() => new()
        {
            Id = Id, Name = Name, ElectricityKw = ElectricityKw, RecipeIds = Recipes,
        };
    }
}
