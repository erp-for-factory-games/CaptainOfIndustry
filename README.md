# ErpForFactoryGames.CaptainOfIndustry

Catalogue extraction for [Captain of Industry](https://www.captain-of-industry.com/)
— products, recipes and buildings, as JSON.

Part of [ERP for Factory Games](https://github.com/erp-for-factory-games/ErpForFactoryGames),
which plans factories from the inputs you have and the outputs you need. Each
supported game gets a standalone repository; see
[ADR-0029](https://github.com/erp-for-factory-games/ErpForFactoryGames/blob/main/docs/adr/0029-standalone-game-libraries-as-nuget-packages.md).

## How it works

Captain of Industry ships its data inside Unity game assemblies rather than as a
readable manifest, so there is nothing to parse off disk directly. The extractor
loads `Mafi.dll`, `Mafi.Core.dll` and `Mafi.Base.dll` from a local install in an
`AssemblyLoadContext`, runs the base mod's `RegisterPrototypes` **outside Unity**,
walks the populated `ProtosDb`, and writes a curated JSON catalogue.

That JSON is what the planner consumes — the ERP side never touches game files.

```powershell
dotnet run --project src/ErpForFactoryGames.CaptainOfIndustry.Extractor -- `
    --install "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry" `
    --out "$env:LOCALAPPDATA\ErpForFactoryGames\coi-catalogue.json"
```

Re-run after each game patch — Mafi ship new recipes and adjust existing ones
with most releases.

Full methodology, JSON shape and known limitations: **[docs/extractor.md](docs/extractor.md)**.

## Status

Moved here from `tools/CaptainOfIndustryExtractor` in the main ERP repository.

**Nothing is published from this repository yet, and the packaging shape is
deliberately undecided.** This is an offline CLI whose output is a JSON file, not
a library with an API — so a NuGet package may be the wrong container for it; a
released `dotnet tool` could fit better. `IsPackable` is `false` until that is
settled in
[ErpForFactoryGames#319](https://github.com/erp-for-factory-games/ErpForFactoryGames/issues/319),
because package ids are permanent once pushed.

The copy under `tools/` in the main repository is still the one wired into that
repo's docs and error messages; retiring it is part of #319.

Captain of Industry has **no save-file parser** and none is planned — ingestion
here is catalogue-only. Compare
[erp-for-factory-games/Satisfactory](https://github.com/erp-for-factory-games/Satisfactory)
and [erp-for-factory-games/OutworldStation](https://github.com/erp-for-factory-games/OutworldStation),
which do read saves.

## Licence

MIT. Not affiliated with MaFi Games.
