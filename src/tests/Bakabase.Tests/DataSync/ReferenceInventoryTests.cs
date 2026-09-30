using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// v3.1 §13 (reference inventory): synced content refers to nothing by a local id. Reflects over the content type
/// graph of every codec this build ships and fails on a member named <c>…Id</c>/<c>…Ids</c> (case-sensitive), on an
/// integer member whose name contains <c>Key</c>, with no exceptions, and on a member typed <c>object</c>, a
/// <see cref="JsonNode"/> or <c>Dictionary&lt;string, object&gt;</c> that is not whitelisted with a reason.
/// </summary>
[TestClass]
public class ReferenceInventoryTests
{
    /// <summary>Opaque members allowed, each with its reason: (declaring type, member) → reason. None today.</summary>
    private static readonly IReadOnlyDictionary<(Type Type, string Member), string> Whitelist =
        new Dictionary<(Type, string), string>();

    private static readonly HashSet<Type> Integers =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
        typeof(ulong), typeof(nint), typeof(nuint), typeof(Int128), typeof(UInt128)
    ];

    /// <summary>Every codec a production assembly of this build declares, found by reflection.</summary>
    private static IReadOnlyList<IDataSyncKindCodec> Codecs()
    {
        // Load the assemblies that declare kinds today; any other loaded Bakabase assembly is scanned too.
        _ = new[] { typeof(ExtensionGroupCodec).Assembly, typeof(DataSyncApplyRunner).Assembly };
        var codecs = new List<IDataSyncKindCodec>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(IsProduction))
        {
            foreach (var type in LoadableTypes(assembly).Where(t =>
                         t is { IsClass: true, IsAbstract: false } && typeof(IDataSyncKindCodec).IsAssignableFrom(t)))
            {
                var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ??
                               (type.GetConstructor(Type.EmptyTypes) is { } ctor ? ctor.Invoke(null) : null);
                Assert.IsNotNull(instance, $"{type.FullName}: a codec is reachable as Instance or constructed bare");
                codecs.Add((IDataSyncKindCodec) instance);
            }
        }

        return codecs;
    }

    private static bool IsProduction(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "";
        return name.StartsWith("Bakabase", StringComparison.Ordinal) &&
               !name.Contains("Test", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    [TestMethod]
    public void Every_shipped_codecs_content_graph_refers_to_nothing_by_local_id()
    {
        var codecs = Codecs();
        CollectionAssert.Contains(codecs.Select(c => c.Descriptor.Kind).ToList(), DataSyncKindIds.ExtensionGroup);
        foreach (var codec in codecs)
        {
            CollectionAssert.Contains(DataSyncKindIds.All.ToList(), codec.Descriptor.Kind,
                $"{codec.GetType().Name}: a kind this build knows");
            var generic = BaseCodecContentType(codec.GetType());
            if (generic is not null)
                Assert.AreEqual(generic, codec.Descriptor.ContentType, $"{codec.Descriptor.Kind}: the descriptor names the codec's own content type");

            var violations = Violations(codec.Descriptor.ContentType);
            Assert.AreEqual(0, violations.Count,
                $"{codec.Descriptor.Kind}: {string.Join("; ", violations)}");
        }
    }

    [TestMethod]
    public void The_inventory_finds_ids_integer_keys_and_unlisted_opaque_members_at_any_depth()
    {
        var violations = Violations(typeof(PlantedContent));

        CollectionAssert.AreEquivalent(new[]
        {
            "PlantedContent.ParentId: named …Id",
            "PlantedContent.SortKey: an integer key",
            "PlantedContent.Extra: an opaque object",
            "PlantedContent.Bag: an opaque object",
            "PlantedChild.TagIds: named …Id",
            "PlantedChild.OrderKeys: an integer key",
            "PlantedChild.Raw: an opaque object",
        }, violations.ToList());
    }

    /// <summary>Violations over the graph reachable from <paramref name="root"/>: members, elements and nested types.</summary>
    private static IReadOnlyList<string> Violations(Type root)
    {
        var violations = new List<string>();
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            if (!seen.Add(type)) continue;
            foreach (var (name, memberType) in Members(type))
            {
                var where = $"{type.Name}.{name}";
                if (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("Ids", StringComparison.Ordinal))
                    violations.Add($"{where}: named …Id");

                var components = Components(memberType).ToList();
                if (name.Contains("Key", StringComparison.Ordinal) && components.Any(Integers.Contains))
                    violations.Add($"{where}: an integer key");
                if (components.Any(IsOpaque) && !Whitelist.ContainsKey((type, name)))
                    violations.Add($"{where}: an opaque object");

                foreach (var component in components.Where(IsOwnModel)) queue.Enqueue(component);
            }
        }

        return violations;
    }

    private static IEnumerable<(string Name, Type Type)> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var property in type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0))
            yield return (property.Name, property.PropertyType);
        foreach (var field in type.GetFields(flags)) yield return (field.Name, field.FieldType);
    }

    /// <summary>The type itself and, through nullables, arrays and generic collections, every type it carries.</summary>
    private static IEnumerable<Type> Components(Type type)
    {
        yield return type;
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            foreach (var inner in Components(underlying)) yield return inner;
        }
        else if (type.IsArray)
        {
            foreach (var inner in Components(type.GetElementType()!)) yield return inner;
        }
        else if (type.IsGenericType && !IsOwnModel(type))
        {
            foreach (var argument in type.GetGenericArguments())
            foreach (var inner in Components(argument))
                yield return inner;
        }
    }

    private static bool IsOpaque(Type type) =>
        type == typeof(object) || typeof(JsonNode).IsAssignableFrom(type) || type == typeof(JsonElement) ||
        type == typeof(JsonDocument) ||
        (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) &&
         type.GetGenericArguments() is [var key, var value] && key == typeof(string) && value == typeof(object));

    /// <summary>A model of this code base, walked into; framework and primitive types are leaves.</summary>
    private static bool IsOwnModel(Type type) =>
        !type.IsPrimitive && !type.IsEnum && type != typeof(string) &&
        (type.Namespace?.StartsWith("Bakabase", StringComparison.Ordinal) ?? false);

    private static Type? BaseCodecContentType(Type codec)
    {
        for (var type = codec.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataSyncKindCodec<>))
                return type.GetGenericArguments()[0];
        }

        return null;
    }

    #region A planted graph the inventory must reject

    private sealed class PlantedContent
    {
        public string Name { get; set; } = "";
        public string Uuid { get; set; } = "";
        public bool Valid { get; set; }
        public string? ParentId { get; set; }
        public int SortKey { get; set; }
        public string OrderKey { get; set; } = "";
        public object? Extra { get; set; }
        public Dictionary<string, object>? Bag { get; set; }
        public List<PlantedChild> Children { get; set; } = [];
    }

    private sealed class PlantedChild
    {
        public List<string> TagIds { get; set; } = [];
        public IReadOnlyList<long?> OrderKeys { get; set; } = [];
        public JsonObject? Raw { get; set; }
        public PlantedContent? Back { get; set; }
    }

    #endregion
}
