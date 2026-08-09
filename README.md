# ErpForFactoryGames.CaptainOfIndustry

Everything [ERP for Factory Games](https://github.com/erp-for-factory-games/ErpForFactoryGames)
needs to know about [Captain of Industry](https://www.captain-of-industry.com/):
its products, recipes and buildings, read straight from an installed copy of the
game and persisted so the planner never needs one.

Onion-layered per [ADR-0004](https://github.com/erp-for-factory-games/ErpForFactoryGames/blob/main/docs/adr/0004-use-onion-architecture.md),
with the CLI as a presentation-layer application released alongside the
libraries.

## Status

Reads the live game: **Captain of Industry 0.8.6 → 227 products, 346 recipes,
84 buildings**, every recipe bound to a machine and carrying a real cycle time.

Not yet published — see [Publishing](#publishing).

## Layout

| Layer | Project | Depends on |
|---|---|---|
| Domain | `ErpForFactoryGames.CaptainOfIndustry.Domain` | nothing |
| Application | `…​.Application` | Domain |
| Infrastructure | `…​.Infrastructure` | Application, Domain |
| Presentation | `…​.Presentation.Cli` | all of the above |

`test/` mirrors `src/` layer for layer.

The split is not ceremony: `MafiCatalogueSource` loads the game's assemblies and
executes its registration code, so it needs Captain of Industry installed.
Everything else — the domain model, the use case, the JSON store — does not. That
is why the whole `ExtractCatalogue` use case is unit-tested with no game present.

## CLI

Ships as a .NET global tool, `erp-coi`. The game agent runs it once to set a
player up; a human can run the same command to see what it found.

```powershell
dotnet tool install -g ErpForFactoryGames.CaptainOfIndustry.Cli

erp-coi catalogue                      # auto-detects the Steam install, writes JSON
erp-coi catalogue --json               # catalogue to stdout, nothing written
erp-coi show --product Product_Steel   # how it's made, where it's used, per-minute rates
```

```
╭───────────┬────────────────────┬───────────────────┬────────────╮
│ Direction │ Recipe             │ Building          │ Per minute │
├───────────┼────────────────────┼───────────────────┼────────────┤
│ makes     │ SteelCastingCooled │ CasterCooledT2    │         24 │
│ uses      │ CompositeCoreBasic │ AssemblyRoboticT2 │         16 │
╰───────────┴────────────────────┴───────────────────┴────────────╯
```

`show` reads persisted JSON, so it works without the game installed.

## Library

```csharp
var extract = new ExtractCatalogue(
    new SteamCoiInstallLocator(),
    new MafiCatalogueSource(),
    new JsonCoiCatalogueStore());

var result = extract.Execute();          // locate → read → persist
result.Catalogue.RecipesProducing("Product_Steel");
result.Catalogue.Recipe("SteelCastingCooled")!.OutputPerMinute("Product_Steel");
```

Install resolution order: an explicit path, then
`ERP_CAPTAIN_OF_INDUSTRY_INSTALL_PATH`, then the usual Steam locations.

## How the catalogue is read

Captain of Industry defines its data in code, not in a readable manifest, so
there is nothing to parse off disk. The infrastructure layer loads `Mafi.dll`,
`Mafi.Core.dll` and `Mafi.Base.dll` into an isolated `AssemblyLoadContext`,
stands up enough of the mod-loading machinery to call
`BaseMod.RegisterPrototypes` **outside Unity**, then walks the populated
`ProtosDb`.

Three things worth knowing, each of which cost a debugging session:

- **Registration stops partway on purpose.** The tail of `RegisterPrototypes`
  reaches for Unity and throws (currently on `ResearchLabsData`). By then every
  product, recipe and machine prototype is registered, so the catalogue is
  complete for planning. It's recorded in `Warnings` rather than swallowed.
- **Durations live on `MachineRecipeBinding`, not on `RecipeProto`.** The recipe
  prototype has no duration member at all. Read the recipe alone and you get a
  catalogue where every cycle time is zero — which looks healthy right up until
  every throughput computes as 0.
- **Names are `LocStr`, whose `ToString()` returns neither the translation nor
  the id.** Reading it as a plain string yields an empty name for every product,
  recipe and building.

Both of the latter two were silently wrong in the original extractor this repo
replaced. There are now assertions against a real installation covering each.

## Publishing

Publishing runs on a version tag, not on every merge — a published package
version is permanent and can't be replaced, so it should be a decision rather
than a side effect.

nuget.org authentication is **trusted publishing** (OIDC): CI exchanges a GitHub
identity token for a short-lived key, so there is no long-lived API secret in the
repository. Set the `NUGET_USER` repository variable to the nuget.org account or
organisation that owns the policy.

## Scope

Catalogue only. A Captain of Industry **save-file parser does not exist yet** —
compare [Satisfactory](https://github.com/erp-for-factory-games/Satisfactory) and
[OutworldStation](https://github.com/erp-for-factory-games/OutworldStation), which
read saves. Adding one here is the next layer-complete addition.

No game assemblies or extracted catalogues are committed.

## Licence

MIT. Not affiliated with MaFi Games.
