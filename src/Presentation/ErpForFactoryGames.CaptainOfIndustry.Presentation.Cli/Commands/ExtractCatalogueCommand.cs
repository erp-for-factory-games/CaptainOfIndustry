using System.ComponentModel;
using ErpForFactoryGames.CaptainOfIndustry.Application;
using ErpForFactoryGames.CaptainOfIndustry.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ErpForFactoryGames.CaptainOfIndustry.Presentation.Cli.Commands;

/// <summary>
/// Reads the catalogue from an installed game and writes it out. This is the
/// once-per-patch setup step the game agent runs for a player.
/// </summary>
public sealed class ExtractCatalogueCommand : Command<ExtractCatalogueCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--install <PATH>")]
        [Description("Game install directory. Auto-detected from Steam when omitted.")]
        public string? InstallDirectory { get; init; }

        [CommandOption("-o|--out <FILE>")]
        [Description("Where to write the catalogue JSON.")]
        public string? OutputPath { get; init; }

        [CommandOption("--json")]
        [Description("Emit the catalogue to stdout instead of a human-readable summary.")]
        public bool Json { get; init; }

        [CommandOption("-v|--verbose")]
        [Description("Show progress while loading the game's assemblies.")]
        public bool Verbose { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var useCase = new ExtractCatalogue(
            new SteamCoiInstallLocator(),
            new MafiCatalogueSource(),
            new JsonCoiCatalogueStore());

        try
        {
            // --json means the caller wants the payload on stdout, so keep every
            // progress line on stderr and don't persist unless asked.
            var result = useCase.Execute(
                settings.InstallDirectory,
                settings.Json && settings.OutputPath is null
                    ? ExtractCatalogue.SkipPersisting
                    : settings.OutputPath,
                settings.Verbose ? message => AnsiConsole.MarkupLineInterpolated($"[dim]{message}[/]") : null);

            if (settings.Json)
            {
                Console.WriteLine(JsonCoiCatalogueStore.Serialize(result.Catalogue));
                return 0;
            }

            Render(result);
            return result.IsUsable ? 0 : 1;
        }
        catch (CoiCatalogueException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] {ex.Message}");
            return 2;
        }
    }

    private static void Render(CatalogueExtractionResult result)
    {
        var catalogue = result.Catalogue;

        AnsiConsole.MarkupLineInterpolated($"[dim]→[/] install [cyan]{result.InstallDirectory}[/]");
        AnsiConsole.MarkupLineInterpolated($"[dim]→[/] Captain of Industry [cyan]{catalogue.GameVersion}[/]");
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("What");
        table.AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("Products", catalogue.Products.Count.ToString());
        table.AddRow("Recipes", catalogue.Recipes.Count.ToString());
        table.AddRow("Buildings", catalogue.Buildings.Count.ToString());
        table.AddRow("Recipes mapped to a building",
            catalogue.Recipes.Count(r => r.BuildingId is not null).ToString());

        AnsiConsole.Write(table);

        if (result.WrittenTo is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]✓[/] wrote [cyan]{result.WrittenTo}[/]");
        }

        foreach (var warning in catalogue.Warnings)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]![/] {warning}");
        }

        // Registration stopping partway is expected and harmless; a recipe
        // pointing at a product that doesn't exist is not.
        var dangling = catalogue.DanglingProductReferences();
        if (dangling.Count > 0)
        {
            var sample = string.Join(", ", dangling.Take(5));
            AnsiConsole.MarkupLineInterpolated(
                $"[red]✗[/] {dangling.Count} recipe product(s) reference no known product: {sample}");
        }
        else if (!result.IsUsable)
        {
            AnsiConsole.MarkupLine("[red]✗[/] catalogue is empty — nothing was registered.");
        }
    }
}
