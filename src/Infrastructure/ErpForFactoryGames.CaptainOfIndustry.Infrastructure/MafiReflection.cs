using System.Collections;
using System.Reflection;
using ErpForFactoryGames.CaptainOfIndustry.Application;

namespace ErpForFactoryGames.CaptainOfIndustry.Infrastructure;

/// <summary>
/// Reflection helpers for Mafi's object model. Kept apart from the reader
/// because the awkwardness here is all Mafi's, not ours.
/// </summary>
internal static class MafiReflection
{
    /// <summary>
    /// Walks the type hierarchy explicitly. The prototype classes shadow members
    /// with <c>new</c>, so a plain <c>GetProperty</c> can bind the wrong one or
    /// throw for ambiguity.
    /// </summary>
    public static object? ReadMember(object? obj, string name)
    {
        if (obj is null) return null;

        for (var t = obj.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var property = t.GetProperty(name, flags);
            if (property is not null) return property.GetValue(obj);

            var field = t.GetField(name, flags);
            if (field is not null) return field.GetValue(obj);
        }

        return null;
    }

    public static string? ReadString(object? obj, string name) => ReadMember(obj, name)?.ToString();

    public static bool ReadBool(object? obj, string name) => ReadMember(obj, name) is bool b && b;

    public static int ReadInt(object? obj, string name) => ReadMember(obj, name) switch
    {
        int i => i,
        long l => (int)l,
        _ => 0,
    };

    /// <summary>
    /// Reads a <c>Mafi.Localization.LocStr</c> member as text.
    /// </summary>
    /// <remarks>
    /// <c>LocStr</c> is a struct of <c>Id</c> plus <c>TranslatedString</c>, and its
    /// <c>ToString()</c> does not yield either — calling <c>ReadString</c> on one
    /// silently produces an empty name, which is exactly what the original
    /// extractor did for every product, recipe and building it emitted.
    /// Falls back to the localisation id, which is still far more use than "".
    /// </remarks>
    public static string ReadLocalisedString(object? obj, string name)
    {
        var locStr = ReadMember(obj, name);
        if (locStr is null) return "";

        if (ReadMember(locStr, "TranslatedString") is string translated
            && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        return ReadMember(locStr, "Id") as string ?? "";
    }

    /// <summary>
    /// Mafi's <c>ImmutableArray&lt;T&gt;</c> is its own type and does not
    /// implement BCL <see cref="IEnumerable"/>. Its <c>ToArray()</c> does return
    /// a regular array, which is the way in.
    /// </summary>
    public static IEnumerable<object?> EnumerateMafiArray(object? mafiArray)
    {
        if (mafiArray is null) yield break;

        var toArray = mafiArray.GetType().GetMethod("ToArray", Type.EmptyTypes);
        if (toArray?.Invoke(mafiArray, null) is not Array array) yield break;

        foreach (var item in array) yield return item;
    }

    /// <summary>BCL enumerable when it is one, otherwise Mafi's array protocol.</summary>
    public static IEnumerable<object?> EnumerateAny(object? collection)
    {
        switch (collection)
        {
            case null:
                yield break;
            case IEnumerable sequence:
                foreach (var item in sequence) yield return item;
                yield break;
            default:
                foreach (var item in EnumerateMafiArray(collection)) yield return item;
                break;
        }
    }

    /// <summary>Every registered prototype of the given type.</summary>
    public static IEnumerable<object> EnumerateProtos(object protosDb, Type protoType)
    {
        var all = protosDb.GetType().GetMethod("All", [typeof(Type)])
                  ?? throw new CoiCatalogueException("ProtosDb.All(Type) not found — the game's API has changed.");

        foreach (var item in (IEnumerable)all.Invoke(protosDb, [protoType])!)
        {
            if (item is not null) yield return item;
        }
    }

    /// <summary>Finds a constructor whose parameters accept the supplied arguments.</summary>
    public static object Construct(Type type, object?[] args)
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var ctor = type.GetConstructors(flags).FirstOrDefault(c =>
        {
            var parameters = c.GetParameters();
            if (parameters.Length != args.Length) return false;

            for (var i = 0; i < parameters.Length; i++)
            {
                if (args[i] is null) continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
            }

            return true;
        }) ?? throw new CoiCatalogueException(
            $"No matching constructor on {type.FullName} for {args.Length} arguments.");

        return ctor.Invoke(args);
    }

    /// <summary>Resolves an explicitly-implemented interface method.</summary>
    public static MethodInfo? FindInterfaceMethod(Type implementation, Type interfaceType, string methodName)
    {
        var map = implementation.GetInterfaceMap(interfaceType);
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (map.InterfaceMethods[i].Name == methodName) return map.TargetMethods[i];
        }

        return null;
    }
}
