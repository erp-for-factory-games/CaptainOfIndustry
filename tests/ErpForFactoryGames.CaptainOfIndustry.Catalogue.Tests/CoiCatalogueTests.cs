using System.Text.Json;
using Xunit;

namespace ErpForFactoryGames.CaptainOfIndustry.Catalogue.Tests;

public class CoiCatalogueQueryTests
{
    private static readonly CoiCatalogue Sample = new()
    {
        GameVersion = "0.6.0",
        Products =
        [
            new CoiProduct { Id = "Coal", Name = "Coal", Kind = "Loose" },
            new CoiProduct { Id = "Iron", Name = "Iron", Kind = "Loose" },
            new CoiProduct { Id = "Steel", Name = "Steel", Kind = "Loose" },
        ],
        Recipes =
        [
            new CoiRecipe
            {
                Id = "SteelT1",
                Building = "SmelterT1",
                DurationTicks = 60,
                Inputs = [new CoiRecipeProduct { ProductId = "Iron", Quantity = 4 },
                          new CoiRecipeProduct { ProductId = "Coal", Quantity = 2 }],
                Outputs = [new CoiRecipeProduct { ProductId = "Steel", Quantity = 3 }],
            },
        ],
        Buildings = [new CoiBuilding { Id = "SmelterT1", ElectricityKw = 120, Recipes = ["SteelT1"] }],
    };

    [Fact]
    public void Finds_recipes_producing_a_product() =>
        Assert.Equal("SteelT1", Assert.Single(Sample.RecipesProducing("Steel")).Id);

    [Fact]
    public void Finds_recipes_consuming_a_product() =>
        Assert.Equal("SteelT1", Assert.Single(Sample.RecipesConsuming("Coal")).Id);

    [Fact]
    public void A_product_that_is_only_an_output_is_consumed_by_nothing() =>
        Assert.Empty(Sample.RecipesConsuming("Steel"));

    [Fact]
    public void Converts_ticks_to_seconds_at_ten_per_second() =>
        Assert.Equal(6.0, Sample.Recipe("SteelT1")!.DurationSeconds);

    [Fact]
    public void Computes_throughput_per_minute()
    {
        var recipe = Sample.Recipe("SteelT1")!;

        // 3 steel per 6-second cycle = 30/min; 4 iron per cycle = 40/min.
        Assert.Equal(30.0, recipe.OutputPerMinute("Steel"));
        Assert.Equal(40.0, recipe.InputPerMinute("Iron"));
    }

    [Fact]
    public void Throughput_of_an_unrelated_product_is_zero() =>
        Assert.Equal(0.0, Sample.Recipe("SteelT1")!.OutputPerMinute("Coal"));

    [Fact]
    public void A_zero_duration_recipe_does_not_divide_by_zero()
    {
        var recipe = new CoiRecipe
        {
            Id = "Instant",
            Outputs = [new CoiRecipeProduct { ProductId = "Steel", Quantity = 1 }],
        };

        Assert.Equal(0.0, recipe.OutputPerMinute("Steel"));
    }

    [Fact]
    public void Lookups_return_null_rather_than_throwing()
    {
        Assert.Null(Sample.Product("Unobtainium"));
        Assert.Null(Sample.Recipe("Nope"));
        Assert.Null(Sample.Building("Nope"));
    }
}

public class CoiCatalogueJsonTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var original = new CoiCatalogue
        {
            GameVersion = "0.6.0",
            ExtractedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
            Products = [new CoiProduct { Id = "Coal", Name = "Coal", Kind = "Loose", IsStorable = true }],
            Recipes =
            [
                new CoiRecipe
                {
                    Id = "R", DurationTicks = 30, Building = "B",
                    Inputs = [new CoiRecipeProduct { ProductId = "Coal", Quantity = 2 }],
                },
            ],
            Buildings = [new CoiBuilding { Id = "B", ElectricityKw = 50, Recipes = ["R"] }],
            Warnings = ["registration stopped midway"],
        };

        var restored = CoiCatalogueJson.Deserialize(CoiCatalogueJson.Serialize(original));

        Assert.Equal(original.GameVersion, restored.GameVersion);
        Assert.Equal(original.ExtractedAt, restored.ExtractedAt);
        Assert.Equal("Coal", Assert.Single(restored.Products).Id);
        Assert.Equal(2, Assert.Single(Assert.Single(restored.Recipes).Inputs).Quantity);
        Assert.Equal(50, Assert.Single(restored.Buildings).ElectricityKw);
        Assert.Equal("registration stopped midway", Assert.Single(restored.Warnings));
    }

    /// <summary>
    /// The planner reads catalogues written by earlier versions, so these key
    /// names are a contract. `items` in particular does not match its property.
    /// </summary>
    [Fact]
    public void Preserves_the_wire_contract_key_names()
    {
        var json = CoiCatalogueJson.Serialize(new CoiCatalogue
        {
            Products = [new CoiProduct { Id = "Coal" }],
            Recipes = [new CoiRecipe { Id = "R" }],
            Buildings = [new CoiBuilding { Id = "B" }],
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("items", out _), "products must serialise as 'items'");
        Assert.True(root.TryGetProperty("recipes", out _));
        Assert.True(root.TryGetProperty("buildings", out _));
        Assert.True(root.TryGetProperty("coiVersion", out _), "game version must serialise as 'coiVersion'");
        Assert.True(root.TryGetProperty("extractedAt", out _));
        Assert.False(root.TryGetProperty("products", out _), "'products' would break existing readers");
    }

    [Fact]
    public void Saves_and_loads_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}", "catalogue.json");
        try
        {
            CoiCatalogueJson.Save(new CoiCatalogue { GameVersion = "1.2.3" }, path);
            Assert.Equal("1.2.3", CoiCatalogueJson.Load(path).GameVersion);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

public class CoiInstallLocatorTests
{
    [Fact]
    public void A_directory_without_the_game_is_not_an_install() =>
        Assert.False(CoiInstallLocator.IsInstallDirectory(Path.GetTempPath()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_paths_resolve_to_nothing(string? path) =>
        Assert.Null(CoiInstallLocator.ManagedDirectory(path));

    [Fact]
    public void Accepts_being_handed_the_managed_directory_itself()
    {
        var managed = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}", "Managed");
        Directory.CreateDirectory(managed);
        try
        {
            Assert.Equal(managed, CoiInstallLocator.ManagedDirectory(managed));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(managed)!, true);
        }
    }

    [Fact]
    public void Finds_managed_under_an_install_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}");
        var managed = Path.Combine(root, "Captain of Industry_Data", "Managed");
        Directory.CreateDirectory(managed);
        try
        {
            Assert.Equal(managed, CoiInstallLocator.ManagedDirectory(root));

            // The directory exists but the assemblies don't.
            Assert.False(CoiInstallLocator.IsInstallDirectory(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

/// <summary>
/// Reads the real catalogue when the game is installed. Skipped otherwise —
/// the game's assemblies are not redistributable and are not committed here.
/// </summary>
public class RealInstallTests
{
    [SkippableFact]
    public void Reads_products_recipes_and_buildings()
    {
        var install = CoiInstallLocator.ResolveInstallDirectory();
        Skip.If(install is null, "Captain of Industry is not installed on this machine.");

        var catalogue = CoiCatalogueReader.Load(install);

        Assert.NotEmpty(catalogue.Products);
        Assert.NotEmpty(catalogue.Recipes);
        Assert.NotEmpty(catalogue.Buildings);
        Assert.NotEqual("unknown", catalogue.GameVersion);

        // Every recipe input and output must name a product that exists.
        var productIds = catalogue.Products.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var dangling = catalogue.Recipes
            .SelectMany(r => r.Inputs.Concat(r.Outputs))
            .Select(p => p.ProductId)
            .Where(id => !string.IsNullOrEmpty(id) && !productIds.Contains(id))
            .Distinct()
            .ToList();

        Assert.Empty(dangling);
    }
}
