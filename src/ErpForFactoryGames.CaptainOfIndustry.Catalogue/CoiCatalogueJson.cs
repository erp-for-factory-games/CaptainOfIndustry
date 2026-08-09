using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErpForFactoryGames.CaptainOfIndustry.Catalogue;

/// <summary>
/// Persists a catalogue as JSON, so it can be read once from a game install and
/// used anywhere afterwards.
/// </summary>
/// <remarks>
/// The planner runs on machines that have no Captain of Industry installation,
/// which is the whole reason this exists: <see cref="CoiCatalogueReader"/> needs
/// the game, this does not. The property names are a wire contract — see
/// <see cref="CoiCatalogue"/>.
/// </remarks>
public static class CoiCatalogueJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(CoiCatalogue catalogue) =>
        JsonSerializer.Serialize(catalogue, Options);

    public static CoiCatalogue Deserialize(string json) =>
        JsonSerializer.Deserialize<CoiCatalogue>(json, Options)
        ?? throw new CoiCatalogueException("Catalogue JSON deserialised to null.");

    public static void Save(CoiCatalogue catalogue, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, catalogue, Options);
    }

    public static CoiCatalogue Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CoiCatalogue>(stream, Options)
               ?? throw new CoiCatalogueException($"Catalogue JSON at '{path}' deserialised to null.");
    }

    /// <summary>Where a persisted catalogue lives by default.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErpForFactoryGames",
        "coi-catalogue.json");
}
