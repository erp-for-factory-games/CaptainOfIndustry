using ErpForFactoryGames.CaptainOfIndustry.Domain;

namespace ErpForFactoryGames.CaptainOfIndustry.Application;

/// <summary>Outcome of an extraction, including where it was written.</summary>
public sealed record CatalogueExtractionResult(
    CoiCatalogue Catalogue,
    string InstallDirectory,
    string? WrittenTo)
{
    /// <summary>
    /// True when the catalogue is complete enough to plan against: it has
    /// recipes, and every product they reference exists.
    /// </summary>
    public bool IsUsable =>
        Catalogue.Recipes.Count > 0
        && Catalogue.Products.Count > 0
        && Catalogue.DanglingProductReferences().Count == 0;
}

/// <summary>
/// Reads the catalogue from an installation and optionally persists it.
/// </summary>
/// <remarks>
/// This is the once-per-patch setup step — the game agent runs it when setting a
/// player up, not on any hot path. Plain constructor injection rather than a
/// mediator: this library has one use case, and a message bus would be
/// ceremony without benefit.
/// </remarks>
public sealed class ExtractCatalogue(
    ICoiInstallLocator locator,
    ICoiCatalogueSource source,
    ICoiCatalogueStore store)
{
    /// <summary>Locates the game, reads the catalogue, and writes it out.</summary>
    /// <param name="installDirectory">Explicit install path; discovered when null.</param>
    /// <param name="outputPath">
    /// Where to persist. Falls back to the store's default; pass
    /// <see cref="SkipPersisting"/> to read without writing.
    /// </param>
    /// <param name="log">Optional progress sink.</param>
    public CatalogueExtractionResult Execute(
        string? installDirectory = null,
        string? outputPath = null,
        Action<string>? log = null)
    {
        var resolved = locator.Resolve(installDirectory)
                       ?? throw new CoiCatalogueException(
                           installDirectory is null
                               ? "No Captain of Industry installation found. Pass an install directory explicitly."
                               : $"'{installDirectory}' is not a Captain of Industry installation.");

        var catalogue = source.Read(resolved, log);

        if (ReferenceEquals(outputPath, SkipPersisting))
        {
            return new CatalogueExtractionResult(catalogue, resolved, WrittenTo: null);
        }

        var destination = outputPath ?? store.DefaultPath;
        store.Save(catalogue, destination);

        return new CatalogueExtractionResult(catalogue, resolved, destination);
    }

    /// <summary>
    /// Sentinel for <c>outputPath</c> meaning "read but do not write". A distinct
    /// object rather than null, because null already means "use the default".
    /// </summary>
    public static readonly string SkipPersisting = new(nameof(SkipPersisting));
}
