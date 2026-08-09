// Thin CLI over the library: read a catalogue from a local install and write it
// as JSON. All the work lives in ErpForFactoryGames.CaptainOfIndustry.Catalogue.

using ErpForFactoryGames.CaptainOfIndustry.Catalogue;

string? installDirectory = null;
string? outputPath = null;
var verbose = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--install" when i + 1 < args.Length:
            installDirectory = args[++i];
            break;
        case "--out" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--verbose" or "-v":
            verbose = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
    }
}

outputPath ??= CoiCatalogueJson.DefaultPath;

if (installDirectory is null)
{
    installDirectory = CoiInstallLocator.ResolveInstallDirectory();
    if (installDirectory is null)
    {
        Console.Error.WriteLine("No Captain of Industry install found. Pass --install <dir>, or set "
                                + CoiInstallLocator.InstallPathEnvironmentVariable + ".");
        PrintUsage();
        return 1;
    }

    Console.WriteLine($"Found install: {installDirectory}");
}

try
{
    var catalogue = CoiCatalogueReader.Load(
        installDirectory,
        verbose ? Console.Error.WriteLine : null);

    CoiCatalogueJson.Save(catalogue, outputPath);

    Console.WriteLine($"  -> wrote {outputPath}");
    Console.WriteLine($"     {catalogue}");

    foreach (var warning in catalogue.Warnings) Console.WriteLine($"     warning: {warning}");

    return 0;
}
catch (CoiCatalogueException ex)
{
    Console.Error.WriteLine($"Extraction failed: {ex.Message}");
    if (verbose && ex.InnerException is not null) Console.Error.WriteLine(ex.InnerException);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Extraction failed: {ex.GetType().FullName}: {ex.Message}");
    if (verbose) Console.Error.WriteLine(ex);
    return 10;
}

static void PrintUsage() => Console.WriteLine("""
    CatalogueDump — writes a JSON catalogue of Captain of Industry's products,
    recipes and buildings by loading the game's assemblies and walking the
    prototype database outside Unity. Re-run once per game patch.

    Usage:
      dotnet run --project tools/CatalogueDump -- [--install <dir>] [--out <file>] [--verbose]

    Options:
      --install <dir>   Game root. Auto-detected from Steam when omitted.
      --out <file>      Output JSON. Defaults to
                        %LocalAppData%\ErpForFactoryGames\coi-catalogue.json
      --verbose, -v     Extra diagnostics on stderr
      --help, -h        Show this help
    """);
