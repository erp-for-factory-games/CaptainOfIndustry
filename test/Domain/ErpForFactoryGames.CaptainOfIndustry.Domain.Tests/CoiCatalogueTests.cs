namespace ErpForFactoryGames.CaptainOfIndustry.Domain.Tests;

public class CoiCatalogueTests
{
    private static readonly CoiCatalogue Sample = new()
    {
        GameVersion = "0.8.6.0",
        Products =
        [
            new CoiProduct { Id = "Coal", Kind = "Loose" },
            new CoiProduct { Id = "Iron", Kind = "Loose" },
            new CoiProduct { Id = "Steel", Kind = "Loose" },
        ],
        Recipes =
        [
            new CoiRecipe
            {
                Id = "SteelT1",
                BuildingId = "SmelterT1",
                DurationTicks = 60,
                Inputs =
                [
                    new CoiRecipeProduct { ProductId = "Iron", Quantity = 4 },
                    new CoiRecipeProduct { ProductId = "Coal", Quantity = 2 },
                ],
                Outputs = [new CoiRecipeProduct { ProductId = "Steel", Quantity = 3 }],
            },
        ],
        Buildings = [new CoiBuilding { Id = "SmelterT1", ElectricityKw = 120, RecipeIds = ["SteelT1"] }],
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

    /// <summary>
    /// Infinity here would silently poison any plan built on it, so a recipe
    /// with no registered duration reports no throughput instead.
    /// </summary>
    [Fact]
    public void A_zero_duration_recipe_reports_no_throughput_rather_than_infinity()
    {
        var recipe = new CoiRecipe
        {
            Id = "Instant",
            Outputs = [new CoiRecipeProduct { ProductId = "Steel", Quantity = 1 }],
        };

        var rate = recipe.OutputPerMinute("Steel");

        Assert.Equal(0.0, rate);
        Assert.False(double.IsInfinity(rate));
    }

    [Fact]
    public void Lookups_return_null_rather_than_throwing()
    {
        Assert.Null(Sample.Product("Unobtainium"));
        Assert.Null(Sample.Recipe("Nope"));
        Assert.Null(Sample.Building("Nope"));
    }

    [Fact]
    public void A_complete_catalogue_has_no_dangling_references() =>
        Assert.Empty(Sample.DanglingProductReferences());

    [Fact]
    public void Detects_a_recipe_referencing_an_unknown_product()
    {
        var broken = Sample with
        {
            Recipes =
            [
                new CoiRecipe
                {
                    Id = "Mystery",
                    DurationTicks = 10,
                    Inputs = [new CoiRecipeProduct { ProductId = "Unobtainium", Quantity = 1 }],
                },
            ],
        };

        Assert.Equal(["Unobtainium"], broken.DanglingProductReferences());
    }
}
