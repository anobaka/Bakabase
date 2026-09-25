using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
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
[TestClass]
public class DataSyncNamingTests
{
    private static readonly Assembly Module = typeof(IDataSyncService).Assembly;

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
        var roots = typeof(DataSyncController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetParameters().Select(p => p.ParameterType).Append(action.ReturnType))
            .ToList();

        var offenders = new List<string>();
        var seen = new HashSet<Type>();
        foreach (var root in roots)
        {
            Walk(root, root.Name, seen, offenders);
        }

        Assert.AreEqual(0, offenders.Count, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// No enum-keyed dictionary (Newtonsoft writes the key by name, the SDK types it by number), no <c>object</c> and
    /// no JSON node (an untyped blob on both ends).
    /// </summary>
    private static void Walk(Type type, string path, HashSet<Type> seen, List<string> offenders)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(object) || typeof(JsonNode).IsAssignableFrom(type) || typeof(JToken).IsAssignableFrom(type))
        {
            offenders.Add($"{path}: {type.Name}");
            return;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(CancellationToken) || !seen.Add(type))
        {
            return;
        }

        if (type.IsGenericType)
        {
            var dictionary = type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType &&
                i.GetGenericTypeDefinition() is var d &&
                (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)));
            if (dictionary?.GetGenericArguments()[0].IsEnum == true)
            {
                offenders.Add($"{path}: enum-keyed {type.Name}");
            }

            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, $"{path}<{argument.Name}>", seen, offenders);
            }
        }

        if (type.IsArray)
        {
            Walk(type.GetElementType()!, $"{path}[]", seen, offenders);
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
                Walk(property.PropertyType, $"{path}.{property.Name}", seen, offenders);
            }
        }
    }
}
