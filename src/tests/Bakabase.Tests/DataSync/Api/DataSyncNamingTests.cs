using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Service.Components;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input.DataSync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// What the frontend is handed about data sync (spec §10.3): names that cannot collide in <c>constants.ts</c>, the
/// build-time constants, and response shapes Newtonsoft writes as plain JSON (§2.10).
/// </summary>
/// <remarks>
/// The naming rule (§2) spans three assemblies: the module's own <c>NamingTests</c> check its enums and service
/// records, and these check the Service's and Business's enums and every data sync type an HTTP request or response
/// reaches, whichever assembly declares it.
/// </remarks>
[TestClass]
public class DataSyncNamingTests
{
    private static readonly Assembly Module = typeof(IDataSyncService).Assembly;
    private static readonly Assembly Business = typeof(DataSyncRuntimeServiceCollectionExtensions).Assembly;

    [TestMethod]
    public void Every_data_sync_enum_and_input_model_of_the_service_carries_the_prefix()
    {
        // constants.ts names an enum by its short name alone, so a prefix is what keeps it from colliding; the input
        // models follow the module's records.
        var offenders = typeof(DataSyncController).Assembly.GetExportedTypes()
            .Where(t => (t.IsEnum && t.Namespace?.Contains("DataSync", StringComparison.Ordinal) == true) ||
                        t.Namespace == typeof(DataSyncSyncNowInputModel).Namespace)
            .Where(t => !t.Name.StartsWith("DataSync", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(", ", offenders));
    }

    [TestMethod]
    public void Every_public_enum_of_the_business_data_sync_components_carries_the_prefix()
    {
        // constants.ts takes the public enums of Business too (F21).
        var offenders = Business.GetExportedTypes()
            .Where(t => t.IsEnum && IsIn(t, "Bakabase.InsideWorld.Business.Components.DataSync"))
            .Where(t => !t.Name.StartsWith("DataSync", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(", ", offenders));
    }

    [TestMethod]
    public void Every_data_sync_type_a_request_or_response_reaches_carries_the_prefix()
    {
        // Records of the module's planning and merging namespaces reach the SDK through the service's records.
        var offenders = ReachableTypes()
            .Where(r => IsIn(r.Type, "Bakabase.Modules.DataSync") || IsIn(r.Type, "Bakabase.Service.Models.Input.DataSync") ||
                        IsIn(r.Type, "Bakabase.InsideWorld.Business.Components.DataSync"))
            .Where(r => !r.Type.Name.StartsWith("DataSync", StringComparison.Ordinal))
            .Select(r => $"{r.Path}: {r.Type.FullName}")
            .ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void The_constants_are_the_values_this_build_uses()
    {
        var constants = BakabaseConstantsGenerator.Generate();

        StringAssert.Contains(constants,
            $"export const DataSyncKinds: readonly string[] = [{string.Join(", ", DataSyncKindIds.All.Select(k => $"\"{k}\""))}] as const;");
        StringAssert.Contains(constants, $"export const DataSyncContractVersion = {DataSyncContract.Version};");
        StringAssert.Contains(constants,
            $"export const DataSyncMaxOptionsPerProperty = {DataSyncLimits.Default.MaxOptionsPerProperty};");

        // The kinds go out in apply order, which the page lists them in.
        CollectionAssert.AreEqual(new[] {DataSyncKindIds.ExtensionGroup, DataSyncKindIds.CustomProperty},
            DataSyncKindIds.All.ToArray());
    }

    [TestMethod]
    public void Every_public_data_sync_enum_reaches_the_frontend()
    {
        var constants = BakabaseConstantsGenerator.Generate();
        var wrong = Module.GetExportedTypes()
            .Where(t => t.IsEnum)
            .Where(t => constants.Split($"export enum {t.Name} {{").Length != 2)
            .Select(t => t.FullName)
            .ToArray();
        Assert.AreEqual(0, wrong.Length, $"missing, or emitted twice: {string.Join(", ", wrong)}");
    }

    [TestMethod]
    public void No_request_or_response_has_a_shape_newtonsoft_writes_badly()
    {
        // No enum-keyed dictionary (Newtonsoft writes the key by name, the SDK types it by number), no object and no
        // JSON node (an untyped blob on both ends).
        var offenders = new List<string>();
        foreach (var (type, path) in ReachableTypes())
        {
            if (type == typeof(object) || typeof(JsonNode).IsAssignableFrom(type) || typeof(JToken).IsAssignableFrom(type))
            {
                offenders.Add($"{path}: {type.Name}");
            }
            else if (type.IsGenericType && type.GetInterfaces().Append(type).Any(i => i.IsGenericType &&
                         i.GetGenericTypeDefinition() is var d &&
                         (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)) &&
                         i.GetGenericArguments()[0].IsEnum))
            {
                offenders.Add($"{path}: enum-keyed {type.Name}");
            }
        }

        Assert.AreEqual(0, offenders.Count, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A type's namespace is <paramref name="ns"/> or one below it; <c>…DataSyncFoo</c> is not below <c>…DataSync</c>.
    /// </summary>
    private static bool IsIn(Type type, string ns) =>
        type.Namespace is { } n && (n == ns || n.StartsWith(ns + ".", StringComparison.Ordinal));

    /// <summary>
    /// Every type the <see cref="DataSyncController"/> actions' parameters and results reach, each with the first
    /// path that reached it.
    /// </summary>
    private static List<(Type Type, string Path)> ReachableTypes()
    {
        var roots = typeof(DataSyncController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetParameters().Select(p => p.ParameterType).Append(action.ReturnType))
            .ToList();

        var reached = new List<(Type, string)>();
        var seen = new HashSet<Type>();
        foreach (var root in roots)
        {
            Walk(root, root.Name, seen, reached);
        }

        return reached;
    }

    private static void Walk(Type type, string path, HashSet<Type> seen, List<(Type, string)> reached)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!seen.Add(type))
        {
            return;
        }

        reached.Add((type, path));
        if (type == typeof(object) || typeof(JsonNode).IsAssignableFrom(type) || typeof(JToken).IsAssignableFrom(type) ||
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(CancellationToken))
        {
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, $"{path}<{argument.Name}>", seen, reached);
            }
        }

        if (type.IsArray)
        {
            Walk(type.GetElementType()!, $"{path}[]", seen, reached);
            return;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) || typeof(Task).IsAssignableFrom(type) ||
            typeof(IActionResult).IsAssignableFrom(type))
        {
            return;
        }

        // The data sync records, the input models, and the response envelopes around them.
        if (type.Namespace?.StartsWith("Bakabase", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Bootstrap.Models.ResponseModels", StringComparison.Ordinal) == true)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Walk(property.PropertyType, $"{path}.{property.Name}", seen, reached);
            }
        }
    }
}
