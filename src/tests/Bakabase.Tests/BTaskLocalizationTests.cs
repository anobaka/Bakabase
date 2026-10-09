using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class BTaskLocalizationTests
{
    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources")
        .AddSingleton<IBakabaseLocalizer, BakabaseLocalizer>().BuildServiceProvider();

    [TestMethod]
    public async Task CompletedProgressAndHistoryContainBothLanguagesWithoutChangingTheServerCulture()
    {
        using var services = Services();
        var localizer = services.GetRequiredService<IBakabaseLocalizer>();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var task = new BTask("SearchIndex", () => localizer.BTask_Name("SearchIndex"));
        var handler = new BTaskHandler(_ => Task.CompletedTask, task, services);
        object?[] arguments = [38743];
        var text = BTaskText.Localize(localizer, "SearchIndex_Completed", arguments);
        arguments[0] = 0;
        await handler.UpdateTask(t => t.SetProcess(text));
        var view = new BTaskViewModel(handler, (string?) null);
        Assert.IsNotNull(view.LocalizedTexts);
        StringAssert.Contains(view.LocalizedTexts["en"].Process!, "38743");
        StringAssert.Contains(view.LocalizedTexts["cn"].Process!, "38743");
        StringAssert.Contains(view.LocalizedTexts["en"].Process!, "Completed");
        StringAssert.Contains(view.LocalizedTexts["cn"].Process!, "完成");
        Assert.AreNotEqual(view.LocalizedTexts["en"].Name, view.LocalizedTexts["cn"].Name);
        Assert.AreEqual(view.LocalizedTexts["cn"].Process, handler.ProcessEvents.Single().LocalizedTexts!["cn"]);
        Assert.AreEqual(culture, CultureInfo.CurrentCulture);
        Assert.AreEqual(uiCulture, CultureInfo.CurrentUICulture);
        Assert.AreEqual(defaultCulture, CultureInfo.DefaultThreadCurrentUICulture);
    }

    [TestMethod]
    public void NestedProgressTranslatesTemplatesButPreservesUserContentAndRawErrors()
    {
        using var services = Services();
        var localizer = services.GetRequiredService<IBakabaseLocalizer>();
        const string path = "/Volumes/NAS/Completed: 中文 archive.zip";
        var nested = BTaskText.Localize(localizer, "BTask_Process_OpeningArchive", path);
        var task = new BTask("literal", () => "User chosen title");
        task.SetProcess(BTaskText.Localize(localizer, "BTask_Process_WorkflowStepDetail", 2, 4, nested));
        task.SetLocalizedError(BTaskText.Localize(localizer, "BTask_Error_DLsiteDirectoryMissing", path), "HTTP 403: raw response");
        task.SetMessage(BTaskText.Localize(localizer, "BTask_Process_Extracting"));
        var handler = new BTaskHandler(_ => Task.CompletedTask, task, services);
        var view = new BTaskViewModel(handler, () => localizer.BTask_FailedToRunTaskDueToConflict(
            localizer.BTask_Name("SearchIndex"), [localizer.BTask_Name("ResourceProfileIndex")]));
        Assert.AreEqual($"Step 2/4 · Opening {path}", view.LocalizedTexts!["en"].Process);
        Assert.AreEqual($"步骤 2/4 · 正在打开 {path}", view.LocalizedTexts["cn"].Process);
        StringAssert.Contains(view.LocalizedTexts["cn"].BriefError!, path);
        Assert.AreNotEqual(view.LocalizedTexts["en"].BriefError, view.LocalizedTexts["cn"].BriefError);
        Assert.AreNotEqual(view.LocalizedTexts["en"].Message, view.LocalizedTexts["cn"].Message);
        Assert.AreNotEqual(view.LocalizedTexts["en"].ReasonForUnableToStart, view.LocalizedTexts["cn"].ReasonForUnableToStart);
        Assert.AreEqual("HTTP 403: raw response", view.Error);
        task.Process = path;
        task.BriefError = "Third-party diagnostic";
        var literal = new BTaskViewModel(handler, (string?) null);
        Assert.IsTrue(literal.LocalizedTexts!.Values.All(x => x.Process == path && x.BriefError == "Third-party diagnostic" && x.Name == "User chosen title"));
    }

    [TestMethod]
    public void FailedProjectionRestoresBothAmbientCultures()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        Assert.ThrowsExactly<InvalidOperationException>(() => BTaskTextCultures.Project<string>(() => throw new InvalidOperationException()));
        Assert.AreEqual(culture, CultureInfo.CurrentCulture);
        Assert.AreEqual(uiCulture, CultureInfo.CurrentUICulture);
    }

    [TestMethod]
    public void DiscoveredProductionTaskNamesAndDescriptionsHaveBothResources()
    {
        using var services = Services();
        var localizer = services.GetRequiredService<IBakabaseLocalizer>();
        // Match runtime discovery across application/module assemblies, including new builders.
        var assemblies = typeof(BTaskLocalizationTests).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("Bakabase.")).Select(Assembly.Load).ToArray();
        var types = assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract &&
            typeof(AbstractPredefinedBTaskBuilder).IsAssignableFrom(t) &&
            t.Namespace?.Contains(".Dev") != true).ToArray();
        Assert.IsTrue(types.Length >= 15, $"Expected production task builders; discovered {types.Length}");
        foreach (var type in types)
        {
            var builder = (AbstractPredefinedBTaskBuilder) ActivatorUtilities.CreateInstance(services, type);
            foreach (var entry in BTaskTextCultures.Project(() => (builder.GetName(), builder.GetDescription())))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value.Item1), $"{type.Name}/{entry.Key}");
                Assert.IsFalse(entry.Value.Item1.StartsWith("BTask_"), $"Missing name {type.Name}/{entry.Key}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value.Item2), $"Missing description {type.Name}/{entry.Key}");
            }
        }
    }

    [TestMethod]
    public void TaskResourcesHaveMatchingKeysAndFormatArgumentsInBothLanguages()
    {
        var resources = new ResourceManager("Bakabase.InsideWorld.Business.Resources.SharedResource", typeof(BakabaseLocalizer).Assembly);
        var en = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var cn = resources.GetResourceSet(CultureInfo.GetCultureInfo("zh-Hans"), true, false)!;
        static bool IsTaskKey(string key) => key.StartsWith("BTask_") || key.StartsWith("SearchIndex_") ||
            key.StartsWith("SyncPathMark_") || key.StartsWith("ResourceSync_") || key is "CopyFiles" or "CopyFile";
        var keys = en.Cast<DictionaryEntry>().Select(x => (string) x.Key).Where(IsTaskKey).ToArray();
        var cnKeys = cn.Cast<DictionaryEntry>().Select(x => (string) x.Key).Where(IsTaskKey).ToArray();
        CollectionAssert.AreEquivalent(keys, cnKeys);
        foreach (var key in keys)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(cn.GetString(key)), key);
            Assert.AreEqual(CompositeFormat.Parse(en.GetString(key)!).MinimumArgumentCount,
                CompositeFormat.Parse(cn.GetString(key)!).MinimumArgumentCount, key);
        }
        using var services = Services();
        var localizer = services.GetRequiredService<IBakabaseLocalizer>();
        foreach (var key in new[] { "CopyFiles", "CopyFile", "BTask_Name_AigcGeneration", "BTask_Name_WorkflowRun", "BTask_Name_ApplySourceMetadata", "BTask_Name_Comparison", "BTask_Description_Comparison" })
            Assert.IsTrue(BTaskTextCultures.Project(() => localizer[key].ResourceNotFound).Values.All(missing => !missing), key);
    }
}
