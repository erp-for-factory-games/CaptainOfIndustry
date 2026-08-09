using ErpForFactoryGames.CaptainOfIndustry.Domain;

namespace ErpForFactoryGames.CaptainOfIndustry.Application.Tests;

/// <summary>
/// The whole use case exercised without Captain of Industry installed. That is
/// the payoff for putting the game behind a port.
/// </summary>
public class ExtractCatalogueTests
{
    private sealed class FakeLocator(string? resolved) : ICoiInstallLocator
    {
        public string? LastRequested { get; private set; }

        public string? Resolve(string? configuredPath = null)
        {
            LastRequested = configuredPath;
            return resolved;
        }

        public bool IsInstallDirectory(string? path) => resolved is not null;
    }

    private sealed class FakeSource(CoiCatalogue catalogue) : ICoiCatalogueSource
    {
        public string? ReadFrom { get; private set; }

        public CoiCatalogue Read(string installDirectory, Action<string>? log = null)
        {
            ReadFrom = installDirectory;
            log?.Invoke("reading");
            return catalogue;
        }
    }

    private sealed class FakeStore : ICoiCatalogueStore
    {
        public string DefaultPath => @"X:\default\coi-catalogue.json";
        public string? SavedTo { get; private set; }
        public CoiCatalogue? Saved { get; private set; }

        public void Save(CoiCatalogue catalogue, string path)
        {
            SavedTo = path;
            Saved = catalogue;
        }

        public CoiCatalogue Load(string path) => Saved ?? new CoiCatalogue();
    }

    private static CoiCatalogue Usable() => new()
    {
        GameVersion = "0.8.6.0",
        Products = [new CoiProduct { Id = "Coal" }],
        Recipes =
        [
            new CoiRecipe
            {
                Id = "R",
                DurationTicks = 10,
                Outputs = [new CoiRecipeProduct { ProductId = "Coal", Quantity = 1 }],
            },
        ],
    };

    [Fact]
    public void Reads_from_the_resolved_install_and_writes_to_the_default_path()
    {
        var locator = new FakeLocator(@"C:\Games\CoI");
        var source = new FakeSource(Usable());
        var store = new FakeStore();

        var result = new ExtractCatalogue(locator, source, store).Execute();

        Assert.Equal(@"C:\Games\CoI", source.ReadFrom);
        Assert.Equal(store.DefaultPath, store.SavedTo);
        Assert.Equal(store.DefaultPath, result.WrittenTo);
        Assert.True(result.IsUsable);
    }

    [Fact]
    public void An_explicit_install_path_is_passed_to_the_locator()
    {
        var locator = new FakeLocator(@"C:\Elsewhere");
        new ExtractCatalogue(locator, new FakeSource(Usable()), new FakeStore())
            .Execute(@"C:\Elsewhere");

        Assert.Equal(@"C:\Elsewhere", locator.LastRequested);
    }

    [Fact]
    public void An_explicit_output_path_overrides_the_default()
    {
        var store = new FakeStore();
        new ExtractCatalogue(new FakeLocator(@"C:\Games\CoI"), new FakeSource(Usable()), store)
            .Execute(outputPath: @"C:\out.json");

        Assert.Equal(@"C:\out.json", store.SavedTo);
    }

    [Fact]
    public void The_skip_sentinel_reads_without_writing()
    {
        var store = new FakeStore();
        var result = new ExtractCatalogue(new FakeLocator(@"C:\Games\CoI"), new FakeSource(Usable()), store)
            .Execute(outputPath: ExtractCatalogue.SkipPersisting);

        Assert.Null(store.SavedTo);
        Assert.Null(result.WrittenTo);
        Assert.NotNull(result.Catalogue);
    }

    /// <summary>
    /// The sentinel must be distinguishable from a real path, not merely equal to
    /// one by value — otherwise a caller passing that literal string would
    /// silently skip persisting.
    /// </summary>
    [Fact]
    public void A_path_that_merely_equals_the_sentinel_text_still_persists()
    {
        var store = new FakeStore();
        var lookalike = new string(ExtractCatalogue.SkipPersisting.ToCharArray());

        new ExtractCatalogue(new FakeLocator(@"C:\Games\CoI"), new FakeSource(Usable()), store)
            .Execute(outputPath: lookalike);

        Assert.Equal(lookalike, store.SavedTo);
    }

    [Fact]
    public void Explains_itself_when_no_install_can_be_found()
    {
        var ex = Assert.Throws<CoiCatalogueException>(() =>
            new ExtractCatalogue(new FakeLocator(null), new FakeSource(Usable()), new FakeStore()).Execute());

        Assert.Contains("No Captain of Industry installation found", ex.Message);
    }

    [Fact]
    public void Names_the_path_when_an_explicit_one_is_wrong()
    {
        var ex = Assert.Throws<CoiCatalogueException>(() =>
            new ExtractCatalogue(new FakeLocator(null), new FakeSource(Usable()), new FakeStore())
                .Execute(@"C:\Nope"));

        Assert.Contains(@"C:\Nope", ex.Message);
    }

    [Fact]
    public void An_empty_catalogue_is_reported_as_unusable()
    {
        var result = new ExtractCatalogue(
                new FakeLocator(@"C:\Games\CoI"),
                new FakeSource(new CoiCatalogue()),
                new FakeStore())
            .Execute();

        Assert.False(result.IsUsable);
    }

    [Fact]
    public void A_catalogue_with_dangling_references_is_reported_as_unusable()
    {
        var broken = new CoiCatalogue
        {
            Products = [new CoiProduct { Id = "Coal" }],
            Recipes =
            [
                new CoiRecipe
                {
                    Id = "R",
                    DurationTicks = 10,
                    Inputs = [new CoiRecipeProduct { ProductId = "Ghost", Quantity = 1 }],
                },
            ],
        };

        var result = new ExtractCatalogue(
                new FakeLocator(@"C:\Games\CoI"), new FakeSource(broken), new FakeStore())
            .Execute();

        Assert.False(result.IsUsable);
    }

    /// <summary>Warnings are expected in normal operation and must not fail the run.</summary>
    [Fact]
    public void Warnings_alone_do_not_make_a_catalogue_unusable()
    {
        var withWarnings = Usable() with { Warnings = ["Registration stopped midway"] };

        var result = new ExtractCatalogue(
                new FakeLocator(@"C:\Games\CoI"), new FakeSource(withWarnings), new FakeStore())
            .Execute();

        Assert.True(result.IsUsable);
        Assert.Single(result.Catalogue.Warnings);
    }
}
