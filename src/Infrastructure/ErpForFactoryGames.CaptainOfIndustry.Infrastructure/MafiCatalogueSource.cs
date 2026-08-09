using System.Reflection;
using System.Runtime.Loader;
using ErpForFactoryGames.CaptainOfIndustry.Application;
using ErpForFactoryGames.CaptainOfIndustry.Domain;
using static ErpForFactoryGames.CaptainOfIndustry.Infrastructure.MafiReflection;

namespace ErpForFactoryGames.CaptainOfIndustry.Infrastructure;

/// <summary>
/// Reads the catalogue out of an installed copy of Captain of Industry.
/// </summary>
/// <remarks>
/// <para>
/// The game defines its data in code, not in a readable manifest, so there is
/// nothing to parse off disk. This loads <c>Mafi.dll</c> / <c>Mafi.Core.dll</c> /
/// <c>Mafi.Base.dll</c> into an isolated <see cref="AssemblyLoadContext"/>,
/// stands up just enough of the mod-loading machinery to call
/// <c>BaseMod.RegisterPrototypes</c> **outside Unity**, then walks the populated
/// prototype database.
/// </para>
/// <para>
/// This therefore executes game code in-process and needs a local installation.
/// That is precisely why it sits behind <see cref="ICoiCatalogueSource"/> and why
/// <see cref="JsonCoiCatalogueStore"/> exists — nothing downstream should inherit
/// those constraints.
/// </para>
/// </remarks>
public sealed class MafiCatalogueSource : ICoiCatalogueSource
{
    public CoiCatalogue Read(string installDirectory, Action<string>? log = null)
    {
        var managed = SteamCoiInstallLocator.ManagedDirectory(installDirectory)
                      ?? throw new CoiCatalogueException(
                          $"'{installDirectory}' does not look like a Captain of Industry install — "
                          + @"expected a 'Captain of Industry_Data\Managed' directory beneath it.");

        var missing = SteamCoiInstallLocator.RequiredAssemblies
            .Where(dll => !File.Exists(Path.Combine(managed, dll)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new CoiCatalogueException(
                $"Missing game assemblies in '{managed}': {string.Join(", ", missing)}.");
        }

        return new PrototypeWalk(managed, log ?? (_ => { })).Run();
    }

    /// <summary>
    /// One pass over the prototype database. Instance state is the loaded
    /// assemblies, so this is deliberately not reused across installs.
    /// </summary>
    private sealed class PrototypeWalk(string managedDirectory, Action<string> log)
    {
        private readonly AssemblyLoadContext _context = CreateLoadContext(managedDirectory);
        private Assembly _mafi = null!;
        private Assembly _core = null!;
        private Assembly _base = null!;

        private static AssemblyLoadContext CreateLoadContext(string managedDirectory)
        {
            var context = new AssemblyLoadContext("coi-catalogue", isCollectible: false);

            // Mafi's assemblies reference each other and a handful of Unity
            // shims; resolve anything they ask for from the same directory.
            context.Resolving += (ctx, name) =>
            {
                var candidate = Path.Combine(managedDirectory, name.Name + ".dll");
                return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
            };

            return context;
        }

        public CoiCatalogue Run()
        {
            _mafi = _context.LoadFromAssemblyPath(Path.Combine(managedDirectory, "Mafi.dll"));
            _core = _context.LoadFromAssemblyPath(Path.Combine(managedDirectory, "Mafi.Core.dll"));
            _base = _context.LoadFromAssemblyPath(Path.Combine(managedDirectory, "Mafi.Base.dll"));

            var gameVersion = _core.GetName().Version?.ToString() ?? "unknown";
            log($"Loaded Mafi/Mafi.Core/Mafi.Base — Captain of Industry build {gameVersion}");

            var (protosDb, warnings) = RegisterPrototypes();
            var buildings = ReadBuildings(protosDb, out var bindings);

            return new CoiCatalogue
            {
                GameVersion = gameVersion,
                ReadAt = DateTimeOffset.UtcNow,
                Products = ReadProducts(protosDb),
                Recipes = ReadRecipes(protosDb, bindings),
                Buildings = buildings,
                Warnings = warnings,
            };
        }

        /// <summary>
        /// How a machine runs a recipe. Duration belongs to this pairing rather
        /// than to the recipe — <c>RecipeProto</c> has no duration of its own.
        /// </summary>
        private readonly record struct RecipeBinding(string BuildingId, int DurationTicks);

        private Type GameType(string name) =>
            _mafi.GetType(name) ?? _core.GetType(name) ?? _base.GetType(name)
            ?? throw new CoiCatalogueException($"Type not found in the game assemblies: {name}");

        /// <summary>
        /// Stands up enough of the mod-loading machinery to invoke
        /// <c>BaseMod.RegisterPrototypes</c> with no Unity present.
        /// </summary>
        private (object ProtosDb, List<string> Warnings) RegisterPrototypes()
        {
            var warnings = new List<string>();

            var baseModType = GameType("Mafi.Base.BaseMod");
            var baseModConfigType = GameType("Mafi.Base.BaseModConfig");
            var manifestType = GameType("Mafi.Core.Mods.ModManifest");
            var protosDbType = GameType("Mafi.Core.Prototypes.ProtosDb");
            var registratorType = GameType("Mafi.Core.Mods.ProtoRegistrator");
            var layoutParserType = GameType("Mafi.Core.Entities.Static.Layout.EntityLayoutParser");
            var modInterfaceType = GameType("Mafi.Core.Mods.IMod");
            var versionType = GameType("Mafi.VersionSlim");

            var version = Construct(versionType, [1, 0, 0, 0]);
            var manifest = BuildManifest(manifestType, version);
            var config = Activator.CreateInstance(baseModConfigType)!;
            var baseMod = Construct(baseModType, [manifest, config]);
            var protosDb = Construct(protosDbType, [baseMod]);
            var layoutParser = Construct(layoutParserType, [protosDb]);
            var registrator = BuildRegistrator(registratorType, protosDb, layoutParser, baseMod, baseModType);

            var register = baseModType.GetMethod("RegisterPrototypes", [registratorType])
                           ?? FindInterfaceMethod(baseModType, modInterfaceType, "RegisterPrototypes")
                           ?? throw new CoiCatalogueException("BaseMod.RegisterPrototypes could not be located.");

            try
            {
                register.Invoke(baseMod, [registrator]);
                log("BaseMod.RegisterPrototypes completed cleanly");
            }
            catch (TargetInvocationException ex)
            {
                // The tail of registration reaches for Unity and throws. By then
                // the product, recipe and machine prototypes are all registered,
                // so a recorded warning beats abandoning a usable catalogue.
                var inner = ex.InnerException ?? ex;
                var message = $"Registration stopped midway: {inner.GetType().Name}: {inner.Message}";
                warnings.Add(message);

                if (inner.InnerException is not null)
                {
                    warnings.Add($"  cause: {inner.InnerException.GetType().Name}: {inner.InnerException.Message}");
                }

                log(message);
            }

            return (protosDb, warnings);
        }

        private object BuildManifest(Type manifestType, object version)
        {
            var ctor = manifestType.GetConstructors().Single();
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                args[i] = parameters[i].Name switch
                {
                    "rootDirectoryPath" => managedDirectory,
                    "id" or "displayName" => "Mafi.Base",
                    "primaryModClassName" => "Mafi.Base.BaseMod",
                    "version" or "minCoiVersion" or "maxVerifiedCoiVersion" => version,
                    "descriptionShort" or "descriptionLong" or "assetBundlesDirOverride" => "",
                    "primaryDlls" or "authors" or "links" or "incompatibleModIds" or "loadErrors" =>
                        EmptyMafiArray(typeof(string)),
                    "mandatoryDependencies" or "optionalDependencies" =>
                        EmptyMafiArray(GameType("Mafi.Core.Mods.ModDependency")),
                    "canAddToSavedGame" or "canRemoveFromSavedGame" => true,
                    "nonLockingDllLoad" => false,
                    _ => parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null,
                };
            }

            return ctor.Invoke(args);
        }

        private object BuildRegistrator(
            Type registratorType, object protosDb, object layoutParser, object baseMod, Type baseModType)
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var ctor = registratorType.GetConstructors(flags).Single();
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var type = parameters[i].ParameterType;

                if (type.IsAssignableFrom(protosDb.GetType())) { args[i] = protosDb; continue; }
                if (type.IsAssignableFrom(layoutParser.GetType())) { args[i] = layoutParser; continue; }

                if (type.IsGenericType && type.Name.StartsWith("ImmutableArray", StringComparison.Ordinal))
                {
                    var element = type.GetGenericArguments()[0];
                    var array = EmptyMafiArray(element);

                    // The registrator expects the mod list to contain BaseMod.
                    if (element.IsAssignableFrom(baseModType))
                    {
                        var add = array.GetType().GetMethod("Add", [element])!;
                        array = add.Invoke(array, [baseMod])!;
                    }

                    args[i] = array;
                    continue;
                }

                args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            return ctor.Invoke(args);
        }

        /// <summary>Builds an empty <c>Mafi.Collections.ImmutableCollections.ImmutableArray&lt;T&gt;</c>.</summary>
        private object EmptyMafiArray(Type elementType)
        {
            var factory = _mafi.GetType("Mafi.Collections.ImmutableCollections.ImmutableArray");

            var create = factory?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.IsGenericMethodDefinition
                                     && m.GetParameters().Length == 0
                                     && m.Name is "Create" or "Empty" or "CreateEmpty");

            if (create is not null) return create.MakeGenericMethod(elementType).Invoke(null, null)!;

            var generic = _mafi.GetType("Mafi.Collections.ImmutableCollections.ImmutableArray`1")
                          ?? throw new CoiCatalogueException("Mafi's ImmutableArray type could not be found.");
            var constructed = generic.MakeGenericType(elementType);

            var field = constructed.GetField("Empty", BindingFlags.Public | BindingFlags.Static);
            if (field is not null) return field.GetValue(null)!;

            var property = constructed.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
            if (property is not null) return property.GetValue(null)!;

            throw new CoiCatalogueException($"Cannot build an empty Mafi ImmutableArray<{elementType.Name}>.");
        }

        private List<CoiProduct> ReadProducts(object protosDb)
        {
            var productType = GameType("Mafi.Core.Products.ProductProto");

            var products = EnumerateProtos(protosDb, productType).Select(product =>
            {
                var type = product.GetType();
                return new CoiProduct
                {
                    Id = ReadString(product, "Id") ?? "",
                    Name = ReadLocalisedString(ReadMember(product, "Strings"), "Name"),
                    Kind = type.Name.EndsWith("ProductProto", StringComparison.Ordinal)
                        ? type.Name[..^"ProductProto".Length]
                        : type.Name,
                    IsStorable = ReadBool(product, "IsStorable"),
                    IsWaste = ReadBool(product, "IsWaste"),
                    Radioactivity = ReadInt(product, "Radioactivity"),
                };
            }).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

            log($"  products:  {products.Count}");
            return products;
        }

        private List<CoiRecipe> ReadRecipes(object protosDb, Dictionary<string, RecipeBinding> bindings)
        {
            var recipeType = GameType("Mafi.Core.Factory.Recipes.RecipeProto");

            var recipes = EnumerateProtos(protosDb, recipeType).Select(recipe =>
            {
                var id = ReadString(recipe, "Id") ?? "";
                var binding = bindings.GetValueOrDefault(id);

                return new CoiRecipe
                {
                    Id = id,
                    Name = ReadLocalisedString(ReadMember(recipe, "Strings"), "Name"),
                    BuildingId = binding.BuildingId,
                    DurationTicks = binding.DurationTicks,
                    Inputs = ReadRecipeProducts(recipe, "AllInputs"),
                    Outputs = ReadRecipeProducts(recipe, "AllOutputs"),
                };
            }).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

            var timed = recipes.Count(r => r.DurationTicks > 0);
            log($"  recipes:   {recipes.Count} ({timed} with a duration)");
            return recipes;
        }

        private static List<CoiRecipeProduct> ReadRecipeProducts(object recipe, string memberName) =>
            EnumerateMafiArray(ReadMember(recipe, memberName))
                .Select(entry => new CoiRecipeProduct
                {
                    ProductId = ReadString(ReadMember(entry, "Product"), "Id") ?? "",
                    Quantity = ReadInt(ReadMember(entry, "Quantity"), "Value"),
                })
                .ToList();

        private List<CoiBuilding> ReadBuildings(object protosDb, out Dictionary<string, RecipeBinding> bindings)
        {
            // MachineProto is the useful "building" for planning; the wider
            // StaticEntityProto also covers terrain, vehicles and decoration.
            var machineType = GameType("Mafi.Core.Factory.Machines.MachineProto");
            var map = new Dictionary<string, RecipeBinding>(StringComparer.Ordinal);

            var buildings = EnumerateProtos(protosDb, machineType).Select(building =>
            {
                var buildingId = ReadString(building, "Id") ?? "";
                var recipeIds = new List<string>();

                // RecipeBindings, not Recipes: the binding is what carries the
                // duration. Reading the recipe list alone yields recipes with no
                // cycle time, and therefore no throughput to plan with.
                foreach (var binding in EnumerateAny(ReadMember(building, "RecipeBindings")))
                {
                    var recipeId = ReadString(ReadMember(binding, "Recipe"), "Id");
                    if (string.IsNullOrEmpty(recipeId)) continue;

                    recipeIds.Add(recipeId);

                    // A recipe belongs to exactly one machine in CoI's model —
                    // tiers are separate recipes — so first wins.
                    map.TryAdd(recipeId, new RecipeBinding(
                        buildingId,
                        ReadInt(ReadMember(binding, "Duration"), "Ticks")));
                }

                recipeIds.Sort(StringComparer.Ordinal);

                return new CoiBuilding
                {
                    Id = buildingId,
                    Name = ReadLocalisedString(ReadMember(building, "Strings"), "Name"),
                    ElectricityKw = ReadInt(ReadMember(building, "ElectricityConsumed"), "Value"),
                    RecipeIds = recipeIds,
                };
            }).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();

            bindings = map;
            log($"  buildings: {buildings.Count} ({map.Count} recipes bound to a machine)");
            return buildings;
        }
    }
}
