using ErpForFactoryGames.CaptainOfIndustry.Presentation.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("erp-coi");

    config.AddCommand<ExtractCatalogueCommand>("catalogue")
        .WithDescription("Read the catalogue from an installed copy of the game.")
        .WithExample("catalogue")
        .WithExample("catalogue", "--install", @"D:\SteamLibrary\steamapps\common\Captain of Industry")
        .WithExample("catalogue", "--json");

    config.AddCommand<ShowCatalogueCommand>("show")
        .WithDescription("Summarise a catalogue that has already been written.")
        .WithExample("show")
        .WithExample("show", "--path", "coi-catalogue.json");
});

return await app.RunAsync(args);
