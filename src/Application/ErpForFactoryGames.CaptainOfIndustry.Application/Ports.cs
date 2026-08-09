using ErpForFactoryGames.CaptainOfIndustry.Domain;

namespace ErpForFactoryGames.CaptainOfIndustry.Application;

/// <summary>Raised when the catalogue cannot be produced at all.</summary>
public sealed class CoiCatalogueException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Locates a Captain of Industry installation on the machine.</summary>
public interface ICoiInstallLocator
{
    /// <summary>
    /// Resolves an installation directory, or null when none can be found.
    /// </summary>
    /// <param name="configuredPath">
    /// An explicit path that takes precedence over discovery, if supplied.
    /// </param>
    string? Resolve(string? configuredPath = null);

    /// <summary>True if the path looks like a usable installation.</summary>
    bool IsInstallDirectory(string? path);
}

/// <summary>
/// Reads the catalogue out of an installed copy of the game.
/// </summary>
/// <remarks>
/// This is the expensive, environment-bound half: implementations need the game
/// present and load its assemblies. Keeping it behind a port is what lets
/// everything else — and every test — run without an installation.
/// </remarks>
public interface ICoiCatalogueSource
{
    /// <summary>Reads the catalogue from the given installation directory.</summary>
    /// <param name="installDirectory">Resolved install path.</param>
    /// <param name="log">Optional progress sink.</param>
    CoiCatalogue Read(string installDirectory, Action<string>? log = null);
}

/// <summary>Persists a catalogue so it can be used without the game installed.</summary>
public interface ICoiCatalogueStore
{
    /// <summary>Where this store writes when no path is given.</summary>
    string DefaultPath { get; }

    void Save(CoiCatalogue catalogue, string path);

    CoiCatalogue Load(string path);
}
