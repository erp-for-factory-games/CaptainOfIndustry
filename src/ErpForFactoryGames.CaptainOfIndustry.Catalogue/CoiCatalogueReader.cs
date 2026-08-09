using System.Reflection;
using System.Runtime.Loader;
using static ErpForFactoryGames.CaptainOfIndustry.Catalogue.MafiReflection;

namespace ErpForFactoryGames.CaptainOfIndustry.Catalogue;

/// <summary>Raised when the catalogue cannot be read at all.</summary>
public sealed class CoiCatalogueException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reads the product, recipe and building catalogue from a Captain of Industry
/// installation.
/// </summary>
/// <remarks>
/// <para>
/// Captain of Industry keeps its data in code rather than in a readable manifest,
/// so there is nothing to parse off disk. This loads the shipped
/// <c>Mafi.dll</c> / <c>Mafi.Core.dll</c> / <c>Mafi.Base.dll</c> into an isolated
/// <see cref="AssemblyLoadContext"/>, runs the base mod's
/// <c>RegisterPrototypes</c> **outside Unity**, then walks the populated
/// prototype database.
/// </para>
/// <para>
/// Consequences worth knowing: this executes game code in-process, it needs a
/// local installation, and it must be re-run after each patch. Callers that want
/// a portable catalogue should read once and persist via
/// <see cref="CoiCatalogueJson"/>.
/// </para>
/// </remarks>
public static class CoiCatalogueReader
{
    /// <summary>Reads the catalogue from a resolved installation.</summary>
    /// <param name="installDirectory">
    /// Game root, or the <c>Managed</c> directory itself. When null, the install
    /// is located via <see cref="CoiInstallLocator.ResolveInstallDirectory"/>.
    /// </param>
    /// <param name="log">Optional progress sink for diagnostics.</param>
    public static CoiCatalogue Load(string? installDirectory = null, Action<string>? log = null)
    {
        var resolved = installDirectory is null
            ? CoiInstallLocator.ResolveInstallDirectory()
            : installDirectory;

        if (resolved is null)
        {
            throw new CoiCatalogueException(
                "No Captain of Industry installation found. Pass the install directory explicitly, "
                + $"or set {CoiInstallLocator.InstallPathEnvironmentVariable}.");
        }

        var managed = CoiInstallLocator.ManagedDirectory(resolved)
                      ?? throw new CoiCatalogueException(
                          $"'{resolved}' does not look like a Captain of Industry install — "
                          + "expected a 'Captain of Industry_Data\\Managed' directory beneath it.");

        var missing = CoiInstallLocator.RequiredAssemblies
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
    /// assemblies, so this is deliberately not reusable across installs.
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
            var buildings = ReadBuildings(protosDb, out var recipeToBuilding);

            return new CoiCatalogue
            {
                ExtractorVersion = typeof(CoiCatalogueReader).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                GameVersion = gameVersion,
                ExtractedAt = DateTimeOffset.UtcNow,
                Products = ReadProducts(protosDb),
                Recipes = ReadRecipes(protosDb, recipeToBuilding),
                Buildings = buildings,
                Warnings = warnings,
            };
        }

        private Type GameType(string name) =>
            _mafi.GetType(name) ?? _core.GetType(name) ?? _base.GetType(name)
            ?? throw new CoiCatalogueException($"Type not found in the game assemblies: {name}");

        /// <summary>
        /// Stands up just enough of the mod-loading machinery to invoke
        /// <c>BaseMod.RegisterPrototypes</c> without Unity present.
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
                // Registration can stop partway on the Unity-dependent tail while
                // still having populated most of the database. A partial catalogue
                // beats no catalogue, so record and continue.
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

                    // The registrator wants the mod list to contain BaseMod itself.
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
                    Name = ReadString(ReadMember(product, "Strings"), "Name") ?? "",
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

        private List<CoiRecipe> ReadRecipes(object protosDb, Dictionary<string, string> recipeToBuilding)
        {
            var recipeType = GameType("Mafi.Core.Factory.Recipes.RecipeProto");

            var recipes = EnumerateProtos(protosDb, recipeType).Select(recipe =>
            {
                var id = ReadString(recipe, "Id") ?? "";
                return new CoiRecipe
                {
                    Id = id,
                    Name = ReadString(ReadMember(recipe, "Strings"), "Name") ?? "",
                    Building = recipeToBuilding.GetValueOrDefault(id),
                    DurationTicks = ReadInt(ReadMember(recipe, "Duration"), "Ticks"),
                    Inputs = ReadRecipeProducts(recipe, "AllInputs"),
                    Outputs = ReadRecipeProducts(recipe, "AllOutputs"),
                };
            }).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

            log($"  recipes:   {recipes.Count}");
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

        private List<CoiBuilding> ReadBuildings(object protosDb, out Dictionary<string, string> recipeToBuilding)
        {
            // MachineProto is the useful "building" for planning; the wider
            // StaticEntityProto also covers terrain, vehicles and decoration.
            var machineType = GameType("Mafi.Core.Factory.Machines.MachineProto");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            var buildings = EnumerateProtos(protosDb, machineType).Select(building =>
            {
                var buildingId = ReadString(building, "Id") ?? "";
                var recipeIds = new List<string>();

                foreach (var recipe in EnumerateAny(ReadMember(building, "Recipes")))
                {
                    var recipeId = ReadString(recipe, "Id");
                    if (string.IsNullOrEmpty(recipeId)) continue;

                    recipeIds.Add(recipeId);

                    // A recipe belongs to exactly one machine in CoI's model —
                    // tiers are modelled as separate recipes — so first wins.
                    map.TryAdd(recipeId, buildingId);
                }

                recipeIds.Sort(StringComparer.Ordinal);

                return new CoiBuilding
                {
                    Id = buildingId,
                    Name = ReadString(ReadMember(building, "Strings"), "Name") ?? "",
                    ElectricityKw = ReadInt(ReadMember(building, "ElectricityConsumed"), "Value"),
                    Recipes = recipeIds,
                };
            }).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();

            recipeToBuilding = map;
            log($"  buildings: {buildings.Count} ({map.Count} recipes mapped to a building)");
            return buildings;
        }
    }
}
