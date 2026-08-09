# ErpForFactoryGames.CaptainOfIndustry

Reads [Captain of Industry](https://www.captain-of-industry.com/)'s product,
recipe and building catalogue in C#, straight from a local game installation —
and persists it as JSON so the planner can use it on machines that have no
installation at all.

Part of [ERP for Factory Games](https://github.com/erp-for-factory-games/ErpForFactoryGames),
which plans factories from the inputs you have and the outputs you need. Each
supported game gets a standalone library; see
[ADR-0029](https://github.com/erp-for-factory-games/ErpForFactoryGames/blob/main/docs/adr/0029-standalone-game-libraries-as-nuget-packages.md).

## Status

Reads the live game: **Captain of Industry 0.8.6 → 227 products, 346 recipes,
84 buildings**, with every recipe mapped to the machine that runs it.

Not yet published to nuget.org — the package prefix reservation is pending
([ErpForFactoryGames#315](https://github.com/erp-for-factory-games/ErpForFactoryGames/issues/315)).

## Usage

```csharp
using ErpForFactoryGames.CaptainOfIndustry.Catalogue;

// Auto-detects a Steam install; pass a path to override.
var catalogue = CoiCatalogueReader.Load();

Console.WriteLine(catalogue);
// CoI 0.8.6.0: 227 products, 346 recipes, 84 buildings, 2 warnings

var steel = catalogue.Recipe("SteelT1")!;
Console.WriteLine(steel.OutputPerMinute("Steel"));       // units/min at full uptime
Console.WriteLine(catalogue.RecipesProducing("Steel"));  // every way to make it
```

Read once, use anywhere:

```csharp
CoiCatalogueJson.Save(catalogue, CoiCatalogueJson.DefaultPath);

// Later, on a machine without the game:
var fromDisk = CoiCatalogueJson.Load(CoiCatalogueJson.DefaultPath);
```

`CoiInstallLocator` resolves in this order: an explicit path, then
`ERP_CAPTAIN_OF_INDUSTRY_INSTALL_PATH`, then the usual Steam locations.

### CLI

`tools/CatalogueDump` is a thin wrapper that writes the JSON:

```powershell
dotnet run --project tools/CatalogueDump -- --verbose
dotnet run --project tools/CatalogueDump -- --install "<CoI dir>" --out catalogue.json
```

Re-run after each game patch — MaFi ship new recipes and adjust existing ones
with most releases.

## How it works

Captain of Industry defines its data in code rather than in a readable manifest,
so there is nothing to parse off disk. The library loads `Mafi.dll`,
`Mafi.Core.dll` and `Mafi.Base.dll` from the installation into an isolated
`AssemblyLoadContext`, stands up just enough of the mod-loading machinery to call
`BaseMod.RegisterPrototypes` **outside Unity**, then walks the populated
`ProtosDb`.

Two consequences worth being explicit about:

- **`CoiCatalogueReader` executes game code in-process** and needs a local
  installation. `CoiCatalogueJson` exists precisely so nothing downstream has to.
- **Registration stops partway on purpose.** The tail of `RegisterPrototypes`
  reaches for Unity and throws (currently on `ResearchLabsData`). By then the
  product, recipe and machine prototypes are already registered, so the catalogue
  is complete for planning purposes. The failure is recorded in
  `catalogue.Warnings` rather than swallowed — a partial catalogue you know about
  beats a total failure.

Full methodology and known limitations: **[docs/extractor.md](docs/extractor.md)**.

## Scope

Catalogue only. Captain of Industry save-file parsing does **not** exist here —
compare [erp-for-factory-games/Satisfactory](https://github.com/erp-for-factory-games/Satisfactory)
and [erp-for-factory-games/OutworldStation](https://github.com/erp-for-factory-games/OutworldStation),
which do read saves.

The older `tools/CaptainOfIndustryExtractor` copy in the main ERP repository is
superseded by this library; retiring it is tracked in
[ErpForFactoryGames#319](https://github.com/erp-for-factory-games/ErpForFactoryGames/issues/319).

## Layout

| Path | |
|---|---|
| `src/ErpForFactoryGames.CaptainOfIndustry.Catalogue/` | the library |
| `tests/` | unit tests, plus a real-install test that skips when the game isn't present |
| `tools/CatalogueDump/` | thin CLI over the library |

No game assemblies or extracted catalogues are committed here.

## Licence

MIT. Not affiliated with MaFi Games.
