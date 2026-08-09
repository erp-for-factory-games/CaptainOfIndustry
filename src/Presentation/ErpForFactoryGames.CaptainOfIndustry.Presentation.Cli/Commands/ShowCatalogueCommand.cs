using System.ComponentModel;
using ErpForFactoryGames.CaptainOfIndustry.Application;
using ErpForFactoryGames.CaptainOfIndustry.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ErpForFactoryGames.CaptainOfIndustry.Presentation.Cli.Commands;

/// <summary>
/// Summarises an already-written catalogue. Needs no game installation — which
/// is the point of persisting one.
/// </summary>
public sealed class ShowCatalogueCommand : Command<ShowCatalogueCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path <FILE>")]
        [Description("Catalogue JSON to read. Defaults to the standard location.")]
        public string? Path { get; init; }

        [CommandOption("--product <ID>")]
        [Description("Show how a single product is made and where it is used.")]
        public string? ProductId { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var store = new JsonCoiCatalogueStore();
        var path = settings.Path ?? store.DefaultPath;

        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] no catalogue at [cyan]{path}[/]");
            AnsiConsole.MarkupLine("[dim]  run [/][cyan]erp-coi catalogue[/][dim] first[/]");
            return 2;
        }

        try
        {
            var catalogue = store.Load(path);
            AnsiConsole.MarkupLineInterpolated($"[dim]→[/] {catalogue}");
            AnsiConsole.WriteLine();

            if (settings.ProductId is null)
            {
                RenderKinds(catalogue);
                return 0;
            }

            return RenderProduct(catalogue, settings.ProductId);
        }
        catch (CoiCatalogueException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] {ex.Message}");
            return 2;
        }
    }

    private static void RenderKinds(Domain.CoiCatalogue catalogue)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Product kind");
        table.AddColumn(new TableColumn("Count").RightAligned());

        foreach (var group in catalogue.Products
                     .GroupBy(p => string.IsNullOrEmpty(p.Kind) ? "(unknown)" : p.Kind)
                     .OrderByDescending(g => g.Count()))
        {
            table.AddRow(Markup.Escape(group.Key), group.Count().ToString());
        }

        AnsiConsole.Write(table);
    }

    private static int RenderProduct(Domain.CoiCatalogue catalogue, string productId)
    {
        var product = catalogue.Product(productId);
        if (product is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] no product [red]{productId}[/]");

            var near = catalogue.Products
                .Where(p => p.Id.Contains(productId, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            if (near.Count > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[dim]  did you mean:[/] {string.Join(", ", near.Select(p => p.Id))}");
            }

            return 2;
        }

        AnsiConsole.MarkupLineInterpolated($"[cyan]{product.Id}[/] [dim]{product.Kind}[/] {product.Name}");
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Direction");
        table.AddColumn("Recipe");
        table.AddColumn("Building");
        table.AddColumn(new TableColumn("Per minute").RightAligned());

        // Ids come from game data, so escape them rather than trusting them not
        // to contain Spectre's markup brackets.
        foreach (var recipe in catalogue.RecipesProducing(product.Id))
        {
            table.AddRow("[green]makes[/]", Markup.Escape(recipe.Id), Building(recipe.BuildingId),
                $"{recipe.OutputPerMinute(product.Id):0.##}");
        }

        foreach (var recipe in catalogue.RecipesConsuming(product.Id))
        {
            table.AddRow("[yellow]uses[/]", Markup.Escape(recipe.Id), Building(recipe.BuildingId),
                $"{recipe.InputPerMinute(product.Id):0.##}");
        }

        static string Building(string? id) => id is null ? "[dim]—[/]" : Markup.Escape(id);

        AnsiConsole.Write(table);
        return 0;
    }
}
