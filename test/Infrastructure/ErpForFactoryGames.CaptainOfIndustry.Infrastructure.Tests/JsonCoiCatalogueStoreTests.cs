using System.Text.Json;
using ErpForFactoryGames.CaptainOfIndustry.Domain;

namespace ErpForFactoryGames.CaptainOfIndustry.Infrastructure.Tests;

public class JsonCoiCatalogueStoreTests
{
    private static CoiCatalogue Sample() => new()
    {
        GameVersion = "0.8.6.0",
        ReadAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
        Products = [new CoiProduct { Id = "Coal", Name = "Coal", Kind = "Loose", IsStorable = true }],
        Recipes =
        [
            new CoiRecipe
            {
                Id = "R", Name = "Make coal", BuildingId = "B", DurationTicks = 30,
                Inputs = [new CoiRecipeProduct { ProductId = "Iron", Quantity = 2 }],
                Outputs = [new CoiRecipeProduct { ProductId = "Coal", Quantity = 1 }],
            },
        ],
        Buildings = [new CoiBuilding { Id = "B", Name = "Smelter", ElectricityKw = 50, RecipeIds = ["R"] }],
        Warnings = ["registration stopped midway"],
    };

    [Fact]
    public void Round_trips_every_field()
    {
        var original = Sample();

        var restored = JsonCoiCatalogueStore.Deserialize(JsonCoiCatalogueStore.Serialize(original));

        Assert.Equal(original.GameVersion, restored.GameVersion);
        Assert.Equal(original.ReadAt, restored.ReadAt);

        var product = Assert.Single(restored.Products);
        Assert.Equal("Coal", product.Id);
        Assert.True(product.IsStorable);

        var recipe = Assert.Single(restored.Recipes);
        Assert.Equal("B", recipe.BuildingId);
        Assert.Equal(30, recipe.DurationTicks);
        Assert.Equal(2, Assert.Single(recipe.Inputs).Quantity);
        Assert.Equal("Coal", Assert.Single(recipe.Outputs).ProductId);

        var building = Assert.Single(restored.Buildings);
        Assert.Equal(50, building.ElectricityKw);
        Assert.Equal(["R"], building.RecipeIds);

        Assert.Equal("registration stopped midway", Assert.Single(restored.Warnings));
    }

    /// <summary>
    /// These key names are a contract with catalogues written by earlier
    /// versions, and two of them deliberately disagree with the domain property
    /// they carry. Renaming either would break existing readers silently.
    /// </summary>
    [Fact]
    public void Pins_the_wire_contract_key_names()
    {
        using var document = JsonDocument.Parse(JsonCoiCatalogueStore.Serialize(Sample()));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("items", out _), "products must serialise as 'items'");
        Assert.True(root.TryGetProperty("coiVersion", out _), "game version must serialise as 'coiVersion'");
        Assert.True(root.TryGetProperty("extractedAt", out _));
        Assert.True(root.TryGetProperty("recipes", out _));
        Assert.True(root.TryGetProperty("buildings", out _));
        Assert.True(root.TryGetProperty("warnings", out _));

        Assert.False(root.TryGetProperty("products", out _), "'products' would break existing readers");
        Assert.False(root.TryGetProperty("gameVersion", out _));

        var recipe = root.GetProperty("recipes")[0];
        Assert.True(recipe.TryGetProperty("building", out _), "BuildingId must serialise as 'building'");

        var building = root.GetProperty("buildings")[0];
        Assert.True(building.TryGetProperty("recipes", out _), "RecipeIds must serialise as 'recipes'");
    }

    [Fact]
    public void Saves_and_loads_a_file()
    {
        var store = new JsonCoiCatalogueStore();
        var path = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}", "catalogue.json");

        try
        {
            store.Save(Sample(), path);
            Assert.Equal("0.8.6.0", store.Load(path).GameVersion);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Default_path_is_under_local_app_data() =>
        Assert.Contains("ErpForFactoryGames", new JsonCoiCatalogueStore().DefaultPath);
}

public class SteamCoiInstallLocatorTests
{
    private readonly SteamCoiInstallLocator _locator = new();

    [Fact]
    public void A_directory_without_the_game_is_not_an_install() =>
        Assert.False(_locator.IsInstallDirectory(Path.GetTempPath()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_paths_resolve_to_nothing(string? path) =>
        Assert.Null(SteamCoiInstallLocator.ManagedDirectory(path));

    [Fact]
    public void Accepts_being_handed_the_managed_directory_itself()
    {
        var managed = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}", "Managed");
        Directory.CreateDirectory(managed);

        try
        {
            Assert.Equal(managed, SteamCoiInstallLocator.ManagedDirectory(managed));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(managed)!, true);
        }
    }

    [Fact]
    public void Finds_managed_under_an_install_root_but_still_requires_the_assemblies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"coi-{Guid.NewGuid():N}");
        var managed = Path.Combine(root, "Captain of Industry_Data", "Managed");
        Directory.CreateDirectory(managed);

        try
        {
            Assert.Equal(managed, SteamCoiInstallLocator.ManagedDirectory(root));
            Assert.False(_locator.IsInstallDirectory(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void An_explicit_path_that_is_not_an_install_resolves_to_null() =>
        Assert.Null(_locator.Resolve(Path.GetTempPath()));
}

/// <summary>
/// Reads the real catalogue when the game is installed. Skipped otherwise — the
/// game's assemblies are not redistributable and are not committed here.
/// </summary>
public class MafiCatalogueSourceTests
{
    [SkippableFact]
    public void Reads_a_usable_catalogue_from_the_installed_game()
    {
        var install = new SteamCoiInstallLocator().Resolve();
        Skip.If(install is null, "Captain of Industry is not installed on this machine.");

        var catalogue = new MafiCatalogueSource().Read(install!);

        Assert.NotEmpty(catalogue.Products);
        Assert.NotEmpty(catalogue.Recipes);
        Assert.NotEmpty(catalogue.Buildings);
        Assert.NotEqual("unknown", catalogue.GameVersion);

        // Every recipe input and output must name a product that exists.
        Assert.Empty(catalogue.DanglingProductReferences());

        // Durations live on the machine-recipe binding, not on RecipeProto.
        // Reading the wrong one yields a catalogue of zero-duration recipes —
        // which looks fine until every throughput comes out as 0.
        Assert.All(catalogue.Recipes, r => Assert.True(
            r.DurationTicks > 0, $"recipe '{r.Id}' has no duration"));

        // Names come from a LocStr, whose ToString() gives neither the
        // translation nor the id. Blank names are the symptom of reading it flat.
        Assert.All(catalogue.Products, p => Assert.False(
            string.IsNullOrWhiteSpace(p.Name), $"product '{p.Id}' has no name"));
        Assert.All(catalogue.Buildings, b => Assert.False(
            string.IsNullOrWhiteSpace(b.Name), $"building '{b.Id}' has no name"));

        // And a real throughput must be computable end to end.
        var producing = catalogue.Recipes.First(r => r.Outputs.Count > 0);
        Assert.True(producing.OutputPerMinute(producing.Outputs[0].ProductId) > 0);

        // And the round trip through the store must not lose anything.
        var restored = JsonCoiCatalogueStore.Deserialize(JsonCoiCatalogueStore.Serialize(catalogue));
        Assert.Equal(catalogue.Products.Count, restored.Products.Count);
        Assert.Equal(catalogue.Recipes.Count, restored.Recipes.Count);
        Assert.Equal(catalogue.Buildings.Count, restored.Buildings.Count);
    }
}
