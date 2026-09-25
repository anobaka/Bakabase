using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.Golden;

/// <summary>
/// §13.4 <c>SchemaGoldenTests</c> (v3.1 §11.2): per real kind, the shape of its content type — every member with its
/// type, nested types and lists included — is snapshotted in <c>Fixtures/DataSync/Golden/{kind}.schema.v{N}.json</c>,
/// with <c>N</c> the codec's schema version. A shape change fails here until someone decides: a semantic change bumps
/// <c>SchemaVersion</c> and adds an <c>Upgrade</c> step (a new golden file), an additive ignorable field only updates
/// the golden. <c>DATASYNC_WRITE_GOLDENS=&lt;dir&gt;</c> writes them again.
/// </summary>
[TestClass]
public class SchemaGoldenTests
{
    /// <summary>
    /// Every real kind codec of the pure engine (<c>Kinds/**</c>), each through its <c>Instance</c> or a parameterless
    /// constructor: a kind added later joins by itself and fails until its golden is written.
    /// </summary>
    private static IReadOnlyList<IDataSyncKindCodec> Codecs() =>
        typeof(ExtensionGroupCodec).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDataSyncKindCodec).IsAssignableFrom(t) &&
                        t.Namespace?.StartsWith("Bakabase.Modules.DataSync.Kinds", StringComparison.Ordinal) == true)
            .Select(t => t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IDataSyncKindCodec ??
                         (t.GetConstructor(Type.EmptyTypes) is { } ctor
                             ? (IDataSyncKindCodec) ctor.Invoke(null)
                             : throw new AssertFailedException($"{t.Name}: give it a static Instance for the goldens.")))
            .OrderBy(c => c.Descriptor.Kind, StringComparer.Ordinal)
            .ToList();

    private static string FileOf(IDataSyncKindCodec codec) =>
        $"{codec.Descriptor.Kind}.schema.v{codec.Descriptor.SchemaVersion}.json";

    [TestMethod]
    public void Every_kinds_content_type_has_the_shape_its_schema_version_promises()
    {
        var codecs = Codecs();
        CollectionAssert.Contains(codecs.Select(c => c.Descriptor.Kind).ToList(), DataSyncKindIds.ExtensionGroup);
        var write = Environment.GetEnvironmentVariable("DATASYNC_WRITE_GOLDENS");
        foreach (var codec in codecs)
        {
            var shape = Shape(codec.Descriptor.ContentType);
            if (write is { Length: > 0 })
            {
                Directory.CreateDirectory(Path.Combine(write, "Golden"));
                File.WriteAllText(Path.Combine(write, "Golden", FileOf(codec)),
                    shape.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
                continue;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DataSync", "Golden", FileOf(codec));
            Assert.IsTrue(File.Exists(path), $"{FileOf(codec)} is missing: write it with DATASYNC_WRITE_GOLDENS.");
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), shape),
                $"{codec.Descriptor.ContentType.Name} changed shape: bump SchemaVersion + add Upgrade, or confirm an " +
                $"additive ignorable field and update {FileOf(codec)}. Now: {shape.ToJsonString()}");
        }
    }

    [TestMethod]
    public void The_shape_names_every_member_with_its_type_and_nullability()
    {
        Assert.AreEqual("""{"extensions":["string"],"name":"string"}""",
            Shape(typeof(ExtensionGroupContentV1)).ToJsonString());
        Assert.AreEqual("""{"items":["integer"],"label":"string?","nested":{"flag":"boolean"},"when":"enum SampleWhen: A, B"}""",
            Shape(typeof(Sample)).ToJsonString());
    }

    /// <summary>
    /// A type's shape: an object of its public instance properties (camelCase, ordinal order), a list as an array of
    /// its item's shape, a dictionary as <c>{"*": value}</c>, and a scalar as its JSON type, <c>?</c> when nullable.
    /// </summary>
    private static JsonNode Shape(Type type, bool nullable = false, int depth = 0)
    {
        if (depth > 16) throw new AssertFailedException($"{type.Name} nests too deep for a content type.");
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Shape(underlying, true, depth);
        string? scalar = type switch
        {
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "boolean",
            _ when type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) => "integer",
            _ when type == typeof(decimal) || type == typeof(double) || type == typeof(float) => "number",
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) => "dateTime",
            _ when type == typeof(JsonObject) || type == typeof(JsonNode) => "json",
            { IsEnum: true } => $"enum {type.Name}: {string.Join(", ", Enum.GetNames(type))}",
            _ => null,
        };
        if (scalar is not null) return JsonValue.Create(nullable ? scalar + "?" : scalar);

        if (type.IsGenericType && type.GetInterfaces().Append(type).Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        {
            return new JsonObject { ["*"] = Shape(type.GetGenericArguments()[1], false, depth + 1) };
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var item = type.IsArray
                ? type.GetElementType()!
                : type.GetInterfaces().Append(type)
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    .GetGenericArguments()[0];
            return new JsonArray(Shape(item, false, depth + 1));
        }

        var nullability = new NullabilityInfoContext();
        var members = new JsonObject();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0)
                     .OrderBy(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.Ordinal))
        {
            var isNullable = nullability.Create(property).ReadState == NullabilityState.Nullable;
            members[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] =
                Shape(property.PropertyType, isNullable && !property.PropertyType.IsValueType, depth + 1);
        }

        return members;
    }

    private enum SampleWhen { A, B }

    private sealed record SampleNested(bool Flag);

    private sealed record Sample(string? Label, IReadOnlyList<int> Items, SampleNested Nested, SampleWhen When);
}
